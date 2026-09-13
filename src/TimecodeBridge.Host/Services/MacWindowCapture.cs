using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TimecodeBridge.Host.Services;

/// <summary>
/// 自プロセスの NSWindow を CGWindowListCreateImage で PNG に撮る。自分のウィンドウだけなので画面収録の許可は要らない。
/// WKWebView は別プロセスで描画されるため、view の cacheDisplay ではなくウィンドウ合成結果を取る。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacWindowCapture
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint WindowListOptionIncludingWindow = 1u << 3;
    private const uint WindowImageBoundsIgnoreFraming = 1u << 0;
    private const uint WindowImageBestResolution = 1u << 3;
    private const uint CFStringEncodingUtf8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    [DllImport(ObjC)] private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, uint listOption, uint windowId, uint imageOption);
    [DllImport(ImageIO)] private static extern IntPtr CGImageDestinationCreateWithData(IntPtr data, IntPtr type, nint count, IntPtr options);
    [DllImport(ImageIO)] private static extern void CGImageDestinationAddImage(IntPtr destination, IntPtr image, IntPtr properties);
    [DllImport(ImageIO)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGImageDestinationFinalize(IntPtr destination);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreateMutable(IntPtr allocator, nint capacity);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

    public static byte[]? CapturePng(IntPtr nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return null;
        var windowNumber = (uint)(long)objc_msgSend(nsWindow, sel_registerName("windowNumber"));
        var nullRect = new CGRect { X = double.PositiveInfinity, Y = double.PositiveInfinity };
        var image = CGWindowListCreateImage(nullRect, WindowListOptionIncludingWindow, windowNumber, WindowImageBoundsIgnoreFraming | WindowImageBestResolution);
        if (image == IntPtr.Zero) return null;

        IntPtr data = IntPtr.Zero, type = IntPtr.Zero, destination = IntPtr.Zero;
        try
        {
            data = CFDataCreateMutable(IntPtr.Zero, 0);
            type = CFStringCreateWithCString(IntPtr.Zero, "public.png", CFStringEncodingUtf8);
            destination = CGImageDestinationCreateWithData(data, type, 1, IntPtr.Zero);
            if (destination == IntPtr.Zero) return null;
            CGImageDestinationAddImage(destination, image, IntPtr.Zero);
            if (!CGImageDestinationFinalize(destination)) return null;
            var length = (int)CFDataGetLength(data);
            var bytes = new byte[length];
            Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, length);
            return bytes;
        }
        finally
        {
            if (destination != IntPtr.Zero) CFRelease(destination);
            if (type != IntPtr.Zero) CFRelease(type);
            if (data != IntPtr.Zero) CFRelease(data);
            CFRelease(image);
        }
    }
}
