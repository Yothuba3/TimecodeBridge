using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Tests.Services;

public class CrashLogTests
{
    [Fact]
    public void Write_例外の内容を指定ファイルへ追記しパスを返す()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tcb2-crash-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "startup-error.log");
        try
        {
            var first = CrashLog.Write("initialization", new InvalidOperationException("boom-1"), path);
            var second = CrashLog.Write("main", new InvalidOperationException("boom-2"), path);

            Assert.Equal(path, first);
            Assert.Equal(path, second);
            var text = File.ReadAllText(path);
            Assert.Contains("stage=initialization", text);
            Assert.Contains("boom-1", text);
            Assert.Contains("stage=main", text);
            Assert.Contains("boom-2", text);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_書けない場所でも例外を出さずnullを返す()
    {
        // ファイルをディレクトリとして使わせて確実に失敗させる
        var dir = Path.Combine(Path.GetTempPath(), $"tcb2-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var blocker = Path.Combine(dir, "blocker");
        File.WriteAllText(blocker, "x");
        try
        {
            var result = CrashLog.Write("main", new Exception("x"), Path.Combine(blocker, "startup-error.log"));

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
