using System.Runtime.InteropServices;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.Services.CoreAudio;

/// <summary>
/// CoreAudioを使用したオーディオプレイバック実装
/// </summary>
public class CoreAudioPlayback : IAudioPlayback
{
    private IntPtr _audioUnit = IntPtr.Zero;
    private IntPtr _audioComponent = IntPtr.Zero;
    private CoreAudioInterop.AudioUnitRenderCallback? _renderCallback;
    private GCHandle _selfHandle;
    private bool _isRunning = false;
    private bool _disposed = false;
    private readonly object _lock = new object();
    private readonly Queue<byte> _audioBuffer = new Queue<byte>();
    private const int MaxBufferSize = 48000 * 2 * 5; // 5秒分のバッファ (48kHz, 16bit)

    /// <summary>
    /// オーディオプレイバックを開始
    /// </summary>
    public void Start(AudioDeviceInfo device)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CoreAudioPlayback));

        if (device == null)
            throw new ArgumentNullException(nameof(device));

        lock (_lock)
        {
            if (_isRunning)
            {
                Stop();
            }

            try
            {
                InitializeAudioUnit(device);
                StartAudioUnit();
                _isRunning = true;
            }
            catch (Exception)
            {
                Cleanup();
                throw;
            }
        }
    }

    /// <summary>
    /// オーディオプレイバックを停止
    /// </summary>
    public void Stop()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CoreAudioPlayback));

        lock (_lock)
        {
            if (!_isRunning)
                return;

            StopAudioUnit();
            Cleanup();
            _audioBuffer.Clear();
            _isRunning = false;
        }
    }

    /// <summary>
    /// オーディオサンプルをデバイスに書き込み
    /// </summary>
    public void WriteSamples(byte[] samples, int offset, int count)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CoreAudioPlayback));

        if (samples == null)
            throw new ArgumentNullException(nameof(samples));

        if (offset < 0 || offset > samples.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (count < 0 || offset + count > samples.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (_lock)
        {
            // バッファオーバーフロー防止
            if (_audioBuffer.Count + count > MaxBufferSize)
            {
                // 古いデータを削除して空きを作る
                int excess = _audioBuffer.Count + count - MaxBufferSize;
                for (int i = 0; i < excess; i++)
                {
                    _audioBuffer.Dequeue();
                }
            }

            // バッファに追加
            for (int i = 0; i < count; i++)
            {
                _audioBuffer.Enqueue(samples[offset + i]);
            }
        }
    }

    /// <summary>
    /// Audio Unitの初期化
    /// </summary>
    private void InitializeAudioUnit(AudioDeviceInfo device)
    {
        // HAL Output コンポーネントの検索
        _audioComponent = CoreAudioInterop.FindHALOutputComponent();
        if (_audioComponent == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to find HAL Output audio component");
        }

        // Audio Unit インスタンスの作成
        int status = CoreAudioInterop.AudioComponentInstanceNew(_audioComponent, out _audioUnit);
        CheckStatus(status, "AudioComponentInstanceNew");

        // Output側のI/Oを有効化（プレイバックモード）
        uint enableIO = 1;
        IntPtr enableIOPtr = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(enableIOPtr, (int)enableIO);
            status = CoreAudioInterop.AudioUnitSetProperty(
                _audioUnit,
                CoreAudioInterop.kAudioOutputUnitProperty_EnableIO,
                CoreAudioInterop.kAudioUnitScope_Output,
                0, // Output bus
                enableIOPtr,
                sizeof(uint));
            CheckStatus(status, "Enable IO on Output");
        }
        finally
        {
            Marshal.FreeHGlobal(enableIOPtr);
        }

        // Input側のI/Oを無効化（プレイバック専用）
        uint disableIO = 0;
        IntPtr disableIOPtr = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(disableIOPtr, (int)disableIO);
            status = CoreAudioInterop.AudioUnitSetProperty(
                _audioUnit,
                CoreAudioInterop.kAudioOutputUnitProperty_EnableIO,
                CoreAudioInterop.kAudioUnitScope_Input,
                1, // Input bus
                disableIOPtr,
                sizeof(uint));
            CheckStatus(status, "Disable IO on Input");
        }
        finally
        {
            Marshal.FreeHGlobal(disableIOPtr);
        }

        // デバイスIDの設定（文字列をUInt32に変換）
        if (uint.TryParse(device.Id, out uint deviceId))
        {
            IntPtr deviceIdPtr = Marshal.AllocHGlobal(sizeof(uint));
            try
            {
                Marshal.WriteInt32(deviceIdPtr, (int)deviceId);
                status = CoreAudioInterop.AudioUnitSetProperty(
                    _audioUnit,
                    CoreAudioInterop.kAudioOutputUnitProperty_CurrentDevice,
                    CoreAudioInterop.kAudioUnitScope_Global,
                    0,
                    deviceIdPtr,
                    sizeof(uint));
                // デバイス設定失敗時もエラーを出さない（デフォルトデバイスを使用）
            }
            finally
            {
                Marshal.FreeHGlobal(deviceIdPtr);
            }
        }

        // ストリームフォーマットの設定（48kHz Mono 16bit PCM）
        var format = CoreAudioInterop.CreateLtcFormat();
        int formatSize = Marshal.SizeOf<CoreAudioInterop.AudioStreamBasicDescription>();
        IntPtr formatPtr = Marshal.AllocHGlobal(formatSize);
        try
        {
            Marshal.StructureToPtr(format, formatPtr, false);
            status = CoreAudioInterop.AudioUnitSetProperty(
                _audioUnit,
                CoreAudioInterop.kAudioUnitProperty_StreamFormat,
                CoreAudioInterop.kAudioUnitScope_Input,
                0, // Output bus の Input scope
                formatPtr,
                (uint)formatSize);
            CheckStatus(status, "Set Stream Format");
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }

        // Render Callbackの設定
        _selfHandle = GCHandle.Alloc(this);
        _renderCallback = RenderCallback;

        var callbackStruct = new AURenderCallbackStruct
        {
            inputProc = Marshal.GetFunctionPointerForDelegate(_renderCallback),
            inputProcRefCon = GCHandle.ToIntPtr(_selfHandle)
        };

        int callbackStructSize = Marshal.SizeOf<AURenderCallbackStruct>();
        IntPtr callbackPtr = Marshal.AllocHGlobal(callbackStructSize);
        try
        {
            Marshal.StructureToPtr(callbackStruct, callbackPtr, false);
            status = CoreAudioInterop.AudioUnitSetProperty(
                _audioUnit,
                CoreAudioInterop.kAudioUnitProperty_SetRenderCallback,
                CoreAudioInterop.kAudioUnitScope_Input,
                0,
                callbackPtr,
                (uint)callbackStructSize);
            CheckStatus(status, "Set Render Callback");
        }
        finally
        {
            Marshal.FreeHGlobal(callbackPtr);
        }

        // Audio Unit の初期化
        status = CoreAudioInterop.AudioUnitInitialize(_audioUnit);
        CheckStatus(status, "AudioUnitInitialize");
    }

    /// <summary>
    /// Audio Unitの開始
    /// </summary>
    private void StartAudioUnit()
    {
        int status = CoreAudioInterop.AudioOutputUnitStart(_audioUnit);
        CheckStatus(status, "AudioOutputUnitStart");
    }

    /// <summary>
    /// Audio Unitの停止
    /// </summary>
    private void StopAudioUnit()
    {
        if (_audioUnit != IntPtr.Zero)
        {
            CoreAudioInterop.AudioOutputUnitStop(_audioUnit);
        }
    }

    /// <summary>
    /// リソースのクリーンアップ
    /// </summary>
    private void Cleanup()
    {
        if (_audioUnit != IntPtr.Zero)
        {
            CoreAudioInterop.AudioUnitUninitialize(_audioUnit);
            CoreAudioInterop.AudioComponentInstanceDispose(_audioUnit);
            _audioUnit = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _renderCallback = null;
        _audioComponent = IntPtr.Zero;
    }

    /// <summary>
    /// Render Callback（CoreAudioからのコールバック）
    /// </summary>
    private int RenderCallback(
        IntPtr inRefCon,
        ref uint ioActionFlags,
        ref CoreAudioInterop.AudioTimeStamp inTimeStamp,
        uint inBusNumber,
        uint inNumberFrames,
        IntPtr ioData)
    {
        try
        {
            if (ioData == IntPtr.Zero)
                return CoreAudioInterop.noErr;

            // AudioBufferListの取得
            var bufferList = Marshal.PtrToStructure<CoreAudioInterop.AudioBufferList>(ioData);
            if (bufferList.Buffer0.Data == IntPtr.Zero || bufferList.Buffer0.DataByteSize == 0)
                return CoreAudioInterop.noErr;

            int bytesNeeded = (int)bufferList.Buffer0.DataByteSize;
            byte[] outputData = new byte[bytesNeeded];

            lock (_lock)
            {
                // バッファからデータを取得
                int bytesAvailable = Math.Min(_audioBuffer.Count, bytesNeeded);
                for (int i = 0; i < bytesAvailable; i++)
                {
                    outputData[i] = _audioBuffer.Dequeue();
                }

                // 不足分はゼロ埋め（無音）
                for (int i = bytesAvailable; i < bytesNeeded; i++)
                {
                    outputData[i] = 0;
                }
            }

            // データをネイティブバッファにコピー
            Marshal.Copy(outputData, 0, bufferList.Buffer0.Data, bytesNeeded);

            return CoreAudioInterop.noErr;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// OSStatusのチェック
    /// </summary>
    private void CheckStatus(int status, string operation)
    {
        if (status != CoreAudioInterop.noErr)
        {
            throw new InvalidOperationException($"CoreAudio operation failed: {operation}, Status: {status}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            if (_isRunning)
            {
                try
                {
                    Stop();
                }
                catch
                {
                    // Dispose中のエラーは無視
                }
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// AURenderCallbackStruct (for SetRenderCallback)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AURenderCallbackStruct
    {
        public IntPtr inputProc;
        public IntPtr inputProcRefCon;
    }
}
