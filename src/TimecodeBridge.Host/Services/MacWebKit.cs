using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TimecodeBridge.Host.Services;

/// <summary>WKWebView の操作のうち Avalonia が公開していないもの。</summary>
[SupportedOSPlatform("macos")]
internal static class MacWebKit
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);

    /// <summary>キャッシュを使わずに現在のページを読み直す(file:// の JS/CSS も再読込される)。</summary>
    public static void ReloadFromOrigin(IntPtr wkWebView) => objc_msgSend(wkWebView, sel_registerName("reloadFromOrigin"));
}
