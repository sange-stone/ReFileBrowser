using System.Text;
using Microsoft.Win32;

namespace ReFileBrowser;

public sealed class Bookmark
{
    public string Name;
    public string Path;
}

/// <summary>
/// 用户数据放在 %APPDATA%\ReFileBrowser\：bookmarks.txt（名称\t路径）、history.txt、settings.ini。
/// 第一次运行时从 360 文件夹导入收藏、地址历史和窗口大小。
/// </summary>
public static class Store
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReFileBrowser");
    private static string BookmarksFile => Path.Combine(Dir, "bookmarks.txt");
    private static string HistoryFile => Path.Combine(Dir, "history.txt");
    private static string SettingsFile => Path.Combine(Dir, "settings.ini");

    private static readonly string Profile360 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "360FileBrowser", "Profile");

    public static List<Bookmark> Bookmarks = new();
    public static List<string> History = new();
    public static Dictionary<string, string> Settings = new(StringComparer.OrdinalIgnoreCase);

    public static event Action BookmarksChanged;

    public static void Load()
    {
        Directory.CreateDirectory(Dir);
        bool first = !File.Exists(SettingsFile);
        if (File.Exists(SettingsFile))
            foreach (var line in File.ReadAllLines(SettingsFile, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i > 0) Settings[line[..i].Trim()] = line[(i + 1)..].Trim();
            }

        if (File.Exists(BookmarksFile))
            Bookmarks = File.ReadAllLines(BookmarksFile, Encoding.UTF8)
                .Select(l => l.Split('\t'))
                .Where(a => a.Length >= 2 && a[1].Length > 0)
                .Select(a => new Bookmark { Name = a[0], Path = a[1] }).ToList();
        else if (first)
            Bookmarks = Import360Bookmarks();

        if (File.Exists(HistoryFile))
            History = File.ReadAllLines(HistoryFile, Encoding.UTF8).Where(l => l.Length > 0).ToList();
        else if (first)
            History = Import360History();

        if (first)
        {
            Import360Window();
            SaveAll();
        }

        PaneSettings.NavPane = GetBool("NavPane", true);
        PaneSettings.PreviewPane = GetBool("PreviewPane", false);
        PaneSettings.DetailsPane = GetBool("DetailsPane", false);
    }

    public static bool GetBool(string key, bool def) => Settings.TryGetValue(key, out var v) ? v == "1" : def;
    public static int GetInt(string key, int def) => Settings.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;
    public static void Set(string key, object value) => Settings[key] = value is bool b ? (b ? "1" : "0") : value.ToString();

    public static void SaveSettings()
    {
        Set("NavPane", PaneSettings.NavPane);
        Set("PreviewPane", PaneSettings.PreviewPane);
        Set("DetailsPane", PaneSettings.DetailsPane);
        WriteSafe(SettingsFile, Settings.Select(kv => kv.Key + "=" + kv.Value));
    }

    public static void SaveBookmarks()
    {
        WriteSafe(BookmarksFile, Bookmarks.Select(b => b.Name.Replace('\t', ' ') + "\t" + b.Path));
        BookmarksChanged?.Invoke();
    }

    public static void SaveHistory() => WriteSafe(HistoryFile, History.Take(30));

    public static void SaveAll() { SaveSettings(); SaveBookmarks(); SaveHistory(); }

    public static void AddHistory(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith("::")) return;
        History.RemoveAll(h => string.Equals(h.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        History.Insert(0, path);
        if (History.Count > 30) History.RemoveRange(30, History.Count - 30);
        SaveHistory();
    }

    private static void WriteSafe(string file, IEnumerable<string> lines)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = file + ".tmp";
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
            File.Move(tmp, file, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---------- 360 导入 ----------

    private static Dictionary<string, string> Read360Ini(string file)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(Profile360, file);
        if (!File.Exists(path)) return map;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = File.ReadAllBytes(path);
        string text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
            : Encoding.GetEncoding(936).GetString(bytes);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ');
            int i = line.IndexOf('=');
            if (i > 0) map[line[..i]] = line[(i + 1)..];
        }
        return map;
    }

    public static List<Bookmark> Import360Bookmarks()
    {
        var ini = Read360Ini("favoriates.ini");
        var list = new List<Bookmark>();
        for (int i = 0; i < 200; i++)
        {
            if (!ini.TryGetValue($"item{i}_path", out var p)) break;
            ini.TryGetValue($"item{i}_name", out var n);
            list.Add(new Bookmark { Name = string.IsNullOrEmpty(n) ? Path.GetFileName(p.TrimEnd('\\')) : n, Path = p });
        }
        return list;
    }

    private static List<string> Import360History()
    {
        var ini = Read360Ini("addr_history.ini");
        var list = new List<string>();
        for (int i = 0; i < 200; i++)
        {
            if (!ini.TryGetValue($"path{i}", out var p)) break;
            if (p.Length > 0) list.Add(p);
        }
        return list;
    }

    private static void Import360Window()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\360FileBrowser");
        if (key == null) return;
        if (key.GetValue("wnd_left") is int l && key.GetValue("wnd_top") is int t
            && key.GetValue("wnd_right") is int r && key.GetValue("wnd_bottom") is int b && r > l && b > t)
        {
            Set("WindowWidth", r - l);
            Set("WindowHeight", b - t);
        }
    }
}
