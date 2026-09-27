using System.Runtime.InteropServices;

namespace TimecodeBridge.Ltc;

/// <summary>
/// libltc 1.3.2 の P/Invoke 宣言。構造体は C 側と同じレイアウト・サイズ(LTCFrame=10, LTCFrameExt=368, SMPTETimecode=13)。
/// </summary>
internal static unsafe class LibltcNative
{
    public const string LibraryName = "libltc";

    [StructLayout(LayoutKind.Sequential, Size = 10)]
    public struct LTCFrame
    {
        public fixed byte Bytes[10];

        // バイト1 = frame_tens(2bit) | dfbit | col_frame | user2(4bit)
        public bool DropFrame => (Bytes[1] & 0x04) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LTCFrameExt
    {
        public LTCFrame Frame;
        public long OffStart;
        public long OffEnd;
        public int Reverse;
        public fixed float BiphaseTics[80];
        public byte SampleMin;
        public byte SampleMax;
        public double Volume;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SMPTETimecode
    {
        public fixed byte Timezone[6];
        public byte Years, Months, Days, Hours, Mins, Secs, Frame;
    }

    static LibltcNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(LibltcNative).Assembly, LibltcLocator.Resolve);
    }

    [DllImport(LibraryName)] public static extern IntPtr ltc_decoder_create(int apv, int queueSize);
    [DllImport(LibraryName)] public static extern int ltc_decoder_free(IntPtr decoder);
    [DllImport(LibraryName)] public static extern void ltc_decoder_write_float(IntPtr decoder, float* buffer, nuint size, long posInfo);
    [DllImport(LibraryName)] public static extern int ltc_decoder_read(IntPtr decoder, LTCFrameExt* frame);
    [DllImport(LibraryName)] public static extern void ltc_frame_to_time(SMPTETimecode* timecode, LTCFrame* frame, int flags);
}
