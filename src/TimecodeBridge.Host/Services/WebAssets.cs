namespace TimecodeBridge.Host.Services;

/// <summary>
/// Web UI(index.html)の所在を決める。開発中は環境変数 TIMECODEBRIDGE_WEB_DIST でリポジトリの
/// src/TimecodeBridge.Web/dist を直接指せる。既定は実行ファイル隣の web/(ビルド時に同梱)。
/// </summary>
public static class WebAssets
{
    public const string EnvironmentVariable = "TIMECODEBRIDGE_WEB_DIST";

    public static string ResolveIndexHtml()
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(Path.Combine(env, "index.html"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "web", "index.html"));

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
        {
            throw new FileNotFoundException(
                "Web UI が見つかりません。src/TimecodeBridge.Web で `npm run build` を実行してから Host をビルドするか、" +
                $"環境変数 {EnvironmentVariable} に dist ディレクトリを指定してください。探索した場所: {string.Join(", ", candidates)}");
        }
        return Path.GetFullPath(found);
    }

    public static Uri IndexUri() => new(ResolveIndexHtml());
}
