using System.Runtime.InteropServices;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.macOS.Services.CoreAudio;

/// <summary>
/// CoreAudioを使用したオーディオキャプチャ実装
/// </summary>
public class CoreAudioCapture : IAudioCapture
{
    private IntPtr _audioUnit = IntPtr.Zero;
    private IntPtr _audioComponent = IntPtr.Zero;
    private CoreAudioInterop.AudioUnitRenderCallback? _renderCallback;
    private GCHandle _selfHandle;
    private bool _isRunning = false;
    private bool _disposed = false;
    private readonly object _lock = new object();

    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
    public event EventHandler<AudioErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// オーディオキャプチャを開始
    /// </summary>
    public void Start(AudioDeviceInfo device)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CoreAudioCapture));

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
            catch (Exception ex)
            {
                Cleanup();
                OnErrorOccurred(new AudioErrorEventArgs($"Failed to start audio capture: {ex.Message}", ex));
                throw;
            }
        }
    }

    /// <summary>
    /// オーディオキャプチャを停止
    /// </summary>
    public void Stop()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CoreAudioCapture));

        lock (_lock)
        {
            if (!_isRunning)
                return;

            StopAudioUnit();
            Cleanup();
            _isRunning = false;
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

        // Input側のI/Oを有効化（キャプチャモード）
        uint enableIO = 1;
        IntPtr enableIOPtr = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(enableIOPtr, (int)enableIO);
            status = CoreAudioInterop.AudioUnitSetProperty(
                _audioUnit,
                CoreAudioInterop.kAudioOutputUnitProperty_EnableIO,
                CoreAudioInterop.kAudioUnitScope_Input,
                1, // Input bus
                enableIOPtr,
                sizeof(uint));
            CheckStatus(status, "Enable IO on Input");
        }
        finally
        {
            Marshal.FreeHGlobal(enableIOPtr);
        }

        // Output側のI/Oを無効化（キャプチャ専用）
        uint disableIO = 0;
        IntPtr disableIOPtr = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(disableIOPtr, (int)disableIO);
            status = CoreAudioInterop.AudioUnitSetProperty(
                _audioUnit,
                CoreAudioInterop.kAudioOutputUnitProperty_EnableIO,
                CoreAudioInterop.kAudioUnitScope_Output,
                0, // Output bus
                disableIOPtr,
                sizeof(uint));
            CheckStatus(status, "Disable IO on Output");
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
                if (status != CoreAudioInterop.noErr)
                {
                    OnErrorOccurred(new AudioErrorEventArgs($"Failed to set device (using default): {status}"));
                }
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
                CoreAudioInterop.kAudioUnitScope_Output,
                1, // Input bus の Output scope
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
            // Audio Unit からサンプルを取得
            var bufferList = new CoreAudioInterop.AudioBufferList
            {
                NumberBuffers = 1
            };

            int bufferListSize = Marshal.SizeOf<CoreAudioInterop.AudioBufferList>();
            IntPtr bufferListPtr = Marshal.AllocHGlobal(bufferListSize);
            try
            {
                Marshal.StructureToPtr(bufferList, bufferListPtr, false);

                uint actionFlags = 0;
                int status = CoreAudioInterop.AudioUnitRender(
                    _audioUnit,
                    ref actionFlags,
                    ref inTimeStamp,
                    inBusNumber,
                    inNumberFrames,
                    bufferListPtr);

                if (status != CoreAudioInterop.noErr)
                {
                    return status;
                }

                // バッファからサンプルを読み取り
                bufferList = Marshal.PtrToStructure<CoreAudioInterop.AudioBufferList>(bufferListPtr);
                if (bufferList.Buffer0.Data != IntPtr.Zero && bufferList.Buffer0.DataByteSize > 0)
                {
                    int sampleCount = (int)(bufferList.Buffer0.DataByteSize / 2); // 16bit = 2 bytes
                    short[] int16Samples = new short[sampleCount];
                    Marshal.Copy(bufferList.Buffer0.Data, int16Samples, 0, sampleCount);

                    // Int16 -> Float変換
                    float[] floatSamples = new float[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        floatSamples[i] = int16Samples[i] / 32768.0f;
                    }

                    // イベント発火
                    OnAudioSamplesAvailable(new AudioSamplesEventArgs(floatSamples));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(bufferListPtr);
            }

            return CoreAudioInterop.noErr;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new AudioErrorEventArgs($"Error in render callback: {ex.Message}", ex));
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
            // エラーコード -50 は TCC権限エラーの可能性
            if (status == -50)
            {
                throw new UnauthorizedAccessException(
                    $"Audio permission denied (TCC). Please grant microphone access in System Settings. Operation: {operation}, Status: {status}");
            }

            throw new InvalidOperationException($"CoreAudio operation failed: {operation}, Status: {status}");
        }
    }

    /// <summary>
    /// AudioSamplesAvailableイベント発火
    /// </summary>
    private void OnAudioSamplesAvailable(AudioSamplesEventArgs args)
    {
        AudioSamplesAvailable?.Invoke(this, args);
    }

    /// <summary>
    /// ErrorOccurredイベント発火
    /// </summary>
    private void OnErrorOccurred(AudioErrorEventArgs args)
    {
        ErrorOccurred?.Invoke(this, args);
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
