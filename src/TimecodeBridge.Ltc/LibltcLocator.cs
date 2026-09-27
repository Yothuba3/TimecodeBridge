using System.Reflection;
using System.Runtime.InteropServices;

namespace TimecodeBridge.Ltc;

/// <summary>
/// 同梱した libltc の読込先を解決する。優先順: 環境変数 TIMECODEBRIDGE_LIBLTC(フルパス) → 実行ファイル隣(.app では Contents/MacOS) → OS 既定の探索。
/// </summary>
public static class LibltcLocator
{
    public const string EnvironmentVariable = "TIMECODEBRIDGE_LIBLTC";

    private static readonly object Lock = new();
    private static IntPtr _handle;

    public static string FileName =>
        OperatingSystem.IsWindows() ? "libltc.dll" :
        OperatingSystem.IsMacOS() ? "libltc.dylib" : "libltc.so";

    public static IEnumerable<string> CandidatePaths()
    {
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env)) yield return env;
        yield return Path.Combine(AppContext.BaseDirectory, FileName);
    }

    internal static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibltcNative.LibraryName) return IntPtr.Zero;

        lock (Lock)
        {
            if (_handle != IntPtr.Zero) return _handle;

            var tried = new List<string>();
            foreach (var path in CandidatePaths())
            {
                tried.Add(path);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out _handle)) return _handle;
            }
            if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out _handle)) return _handle;

            throw new DllNotFoundException(
                $"libltc が見つかりません。探索した場所: {string.Join(", ", tried)}。" +
                $"native/libltc/build-macos.sh(または build-windows.cmd)で生成した {FileName} を実行ファイルと同じ場所に置くか、" +
                $"環境変数 {EnvironmentVariable} でフルパスを指定してください。");
        }
    }
}
