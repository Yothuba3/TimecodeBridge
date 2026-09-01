using System.Runtime.InteropServices;

namespace TimecodeBridge.App.Services.CoreAudio;

/// <summary>
/// CoreAudio P/Invoke署名とネイティブ構造体定義
/// </summary>
public static class CoreAudioInterop
{
    private const string CoreAudioFramework = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string AudioToolboxFramework = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    #region Constants

    // OSStatus
    public const int noErr = 0;

    // Audio Component Types
    public const uint kAudioUnitType_Output = 0x61756F75; // 'auou'
    public const uint kAudioUnitType_MusicDevice = 0x61756D75; // 'aumu'
    public const uint kAudioUnitType_MusicEffect = 0x61756D66; // 'aumf'
    public const uint kAudioUnitType_FormatConverter = 0x61756663; // 'aufc'
    public const uint kAudioUnitType_Effect = 0x61756678; // 'aufx'
    public const uint kAudioUnitType_Mixer = 0x61756D78; // 'aumx'
    public const uint kAudioUnitType_Panner = 0x6175706E; // 'aupn'
    public const uint kAudioUnitType_Generator = 0x6175676E; // 'augn'
    public const uint kAudioUnitType_OfflineEffect = 0x61756F6C; // 'auol'

    // Audio Component Subtypes
    public const uint kAudioUnitSubType_HALOutput = 0x6168616C; // 'ahal'
    public const uint kAudioUnitSubType_DefaultOutput = 0x64656620; // 'def '
    public const uint kAudioUnitSubType_SystemOutput = 0x73797320; // 'sys '
    public const uint kAudioUnitSubType_GenericOutput = 0x67656E72; // 'genr'

    // Audio Component Manufacturer
    public const uint kAudioUnitManufacturer_Apple = 0x6170706C; // 'appl'

    // Audio Format IDs
    public const uint kAudioFormatLinearPCM = 0x6C70636D; // 'lpcm'

    // Audio Format Flags
    public const uint kAudioFormatFlagIsFloat = (1u << 0);
    public const uint kAudioFormatFlagIsBigEndian = (1u << 1);
    public const uint kAudioFormatFlagIsSignedInteger = (1u << 2);
    public const uint kAudioFormatFlagIsPacked = (1u << 3);
    public const uint kAudioFormatFlagIsAlignedHigh = (1u << 4);
    public const uint kAudioFormatFlagIsNonInterleaved = (1u << 5);
    public const uint kAudioFormatFlagIsNonMixable = (1u << 6);

    // Audio Unit Property IDs
    public const uint kAudioUnitProperty_StreamFormat = 8;
    public const uint kAudioOutputUnitProperty_EnableIO = 2003;
    public const uint kAudioOutputUnitProperty_CurrentDevice = 2000;
    public const uint kAudioUnitProperty_SetRenderCallback = 23;
    public const uint kAudioUnitProperty_MaximumFramesPerSlice = 14;

    // Audio Unit Scopes
    public const uint kAudioUnitScope_Global = 0;
    public const uint kAudioUnitScope_Input = 1;
    public const uint kAudioUnitScope_Output = 2;

    // Audio Time Stamp Flags
    public const uint kAudioTimeStampSampleTimeValid = (1u << 0);
    public const uint kAudioTimeStampHostTimeValid = (1u << 1);
    public const uint kAudioTimeStampRateScalarValid = (1u << 2);
    public const uint kAudioTimeStampWordClockTimeValid = (1u << 3);
    public const uint kAudioTimeStampSMPTETimeValid = (1u << 4);

    // Audio Unit Render Action Flags
    public const uint kAudioUnitRenderAction_PreRender = (1u << 2);
    public const uint kAudioUnitRenderAction_PostRender = (1u << 3);
    public const uint kAudioUnitRenderAction_OutputIsSilence = (1u << 4);
    public const uint kAudioOfflineUnitRenderAction_Preflight = (1u << 5);
    public const uint kAudioOfflineUnitRenderAction_Render = (1u << 6);
    public const uint kAudioOfflineUnitRenderAction_Complete = (1u << 7);

    // AudioObjectPropertySelector for device enumeration
    public const uint kAudioHardwarePropertyDevices = 0x64657623; // 'dev#'
    public const uint kAudioObjectPropertyName = 0x6C6E616D; // 'lnam'
    public const uint kAudioDevicePropertyStreams = 0x73746D23; // 'stm#'
    public const uint kAudioDevicePropertyScopeInput = 0x696E7074; // 'inpt'
    public const uint kAudioDevicePropertyScopeOutput = 0x6F757470; // 'outp'

    // AudioObjectPropertyScope
    public const uint kAudioObjectPropertyScopeGlobal = 0x676C6F62; // 'glob'
    public const uint kAudioObjectPropertyScopeInput = 0x696E7074; // 'inpt'
    public const uint kAudioObjectPropertyScopeOutput = 0x6F757470; // 'outp'

    // AudioObjectPropertyElement
    public const uint kAudioObjectPropertyElementMain = 0; // 'main'

    // AudioObjectID
    public const uint kAudioObjectSystemObject = 1;

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioComponentDescription
    {
        public uint ComponentType;
        public uint ComponentSubType;
        public uint ComponentManufacturer;
        public uint ComponentFlags;
        public uint ComponentFlagsMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatID;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SMPTETime
    {
        public short Subframes;
        public short SubframeDivisor;
        public uint Counter;
        public uint Type;
        public uint Flags;
        public short Hours;
        public short Minutes;
        public short Seconds;
        public short Frames;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioTimeStamp
    {
        public double SampleTime;
        public ulong HostTime;
        public double RateScalar;
        public ulong WordClockTime;
        public SMPTETime SMPTETime;
        public uint Flags;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBuffer
    {
        public uint NumberChannels;
        public uint DataByteSize;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBufferList
    {
        public uint NumberBuffers;
        public AudioBuffer Buffer0; // First buffer (fixed array simulation)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioObjectPropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    #endregion

    #region Delegates

    /// <summary>
    /// Audio Unit Render Callback デリゲート
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int AudioUnitRenderCallback(
        IntPtr inRefCon,
        ref uint ioActionFlags,
        ref AudioTimeStamp inTimeStamp,
        uint inBusNumber,
        uint inNumberFrames,
        IntPtr ioData);

    #endregion

    #region P/Invoke Methods

    [DllImport(AudioToolboxFramework)]
    public static extern IntPtr AudioComponentFindNext(IntPtr inComponent, ref AudioComponentDescription inDesc);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioComponentInstanceNew(IntPtr inComponent, out IntPtr outInstance);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioComponentInstanceDispose(IntPtr inInstance);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioUnitInitialize(IntPtr inUnit);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioUnitUninitialize(IntPtr inUnit);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioUnitSetProperty(
        IntPtr inUnit,
        uint inID,
        uint inScope,
        uint inElement,
        IntPtr inData,
        uint inDataSize);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioUnitGetProperty(
        IntPtr inUnit,
        uint inID,
        uint inScope,
        uint inElement,
        IntPtr outData,
        ref uint ioDataSize);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioOutputUnitStart(IntPtr ci);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioOutputUnitStop(IntPtr ci);

    [DllImport(AudioToolboxFramework)]
    public static extern int AudioUnitRender(
        IntPtr inUnit,
        ref uint ioActionFlags,
        ref AudioTimeStamp inTimeStamp,
        uint inOutputBusNumber,
        uint inNumberFrames,
        IntPtr ioData);

    [DllImport(CoreAudioFramework)]
    public static extern int AudioObjectGetPropertyDataSize(
        uint inObjectID,
        ref AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize,
        IntPtr inQualifierData,
        out uint outDataSize);

    [DllImport(CoreAudioFramework)]
    public static extern int AudioObjectGetPropertyData(
        uint inObjectID,
        ref AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize,
        IntPtr inQualifierData,
        ref uint ioDataSize,
        IntPtr outData);

    [DllImport(CoreAudioFramework)]
    public static extern int AudioObjectHasProperty(
        uint inObjectID,
        ref AudioObjectPropertyAddress inAddress);

    #endregion

    #region Helper Methods

    /// <summary>
    /// 48kHz Mono 16bit PCM フォーマット（LTC標準）を作成
    /// </summary>
    public static AudioStreamBasicDescription CreateLtcFormat()
    {
        return new AudioStreamBasicDescription
        {
            SampleRate = 48000.0,
            FormatID = kAudioFormatLinearPCM,
            FormatFlags = kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked,
            BytesPerPacket = 2,
            FramesPerPacket = 1,
            BytesPerFrame = 2,
            ChannelsPerFrame = 1,
            BitsPerChannel = 16,
            Reserved = 0
        };
    }

    /// <summary>
    /// HAL Output Audio Component の検索
    /// </summary>
    public static IntPtr FindHALOutputComponent()
    {
        var desc = new AudioComponentDescription
        {
            ComponentType = kAudioUnitType_Output,
            ComponentSubType = kAudioUnitSubType_HALOutput,
            ComponentManufacturer = kAudioUnitManufacturer_Apple,
            ComponentFlags = 0,
            ComponentFlagsMask = 0
        };

        return AudioComponentFindNext(IntPtr.Zero, ref desc);
    }

    #endregion
}
