using System.Text.Json;

namespace TimecodeBridge.Host.Services;

/// <summary>
/// 最近使ったプロジェクトの一覧(settings.json)。v3 の保存先が無いときは v2(TimecodeBridge2)の設定を初回だけ取り込む。
/// アプリ設定として持つのはこれだけで、デバイス選択などはプロジェクト側(SourceSettings)に入る。
/// </summary>
public sealed class RecentProjectsStore
{
    public const int Max = 10;
    private readonly string _path;
    private readonly List<string> _items;

    public RecentProjectsStore(string? path = null, string? legacyPath = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "settings.json");
        legacyPath ??= Path.Combine(AppPaths.DataDirectoryFor(AppPaths.LegacyAppFolderName), "settings.json");
        _items = Load(_path) ?? Load(legacyPath) ?? new List<string>();
    }

    public IReadOnlyList<string> Items => _items;

    public event Action? Changed;

    public void Add(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        _items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, full);
        if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
        Save();
        Changed?.Invoke();
    }

    public void Remove(string filePath)
    {
        if (_items.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase)) == 0) return;
        Save();
        Changed?.Invoke();
    }

    private static List<string>? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var doc = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path), Json);
            return doc?.RecentProjects?.Where(p => !string.IsNullOrWhiteSpace(p)).Take(Max).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsFile { RecentProjects = _items }, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 設定が書けなくても運用は続ける
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class SettingsFile
    {
        public List<string>? RecentProjects { get; set; }
    }
}
