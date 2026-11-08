using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ReFileBrowser;

/// <summary>
/// 系统集成：接管 Win+E / 双击文件夹、开机预加载、桌面快捷方式。
/// 接管方式和 360 一样：
///   HKLM\Software\Classes\Folder\shell\opennewwindow\command (默认) = "ReFileBrowser.exe"，删掉 DelegateExecute（Win+E 走这里）
///   HKCU\Software\Classes\Folder\shell (默认) = ReFileBrowser，子键 command = "ReFileBrowser.exe" "%1"（双击文件夹走这里）
/// 第一次接管前把原值写到 %APPDATA%\ReFileBrowser\registry-backup.txt，恢复时原样写回。
/// </summary>
public static class Integration
{
    private const string HklmCmd = @"Software\Classes\Folder\shell\opennewwindow\command";
    private const string HkcuShell = @"Software\Classes\Folder\shell";
    private const string VerbName = "ReFileBrowser";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Missing = "<missing>";

    private static string Exe => Environment.ProcessPath;
    private static string BackupFile => Path.Combine(Store.Dir, "registry-backup.txt");

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static bool IsDefault()
    {
        using var k = Registry.LocalMachine.OpenSubKey(HklmCmd);
        return (k?.GetValue("") as string)?.Contains("ReFileBrowser.exe", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static void RunElevated(string arg)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Exe, arg) { UseShellExecute = true, Verb = "runas" });
            p?.WaitForExit(15000);
        }
        catch (System.ComponentModel.Win32Exception) { } // 用户在 UAC 弹窗里点了取消
    }

    /// <summary>在管理员进程里执行（ReFileBrowser.exe --register）。</summary>
    public static int Register()
    {
        Directory.CreateDirectory(Store.Dir);
        if (!File.Exists(BackupFile))
        {
            var sb = new StringBuilder();
            using (var k = Registry.LocalMachine.OpenSubKey(HklmCmd))
            {
                sb.AppendLine("HKLM_Default=" + (k?.GetValue("") as string ?? Missing));
                sb.AppendLine("HKLM_DelegateExecute=" + (k?.GetValue("DelegateExecute") as string ?? Missing));
            }
            using (var k = Registry.CurrentUser.OpenSubKey(HkcuShell))
                sb.AppendLine("HKCU_ShellDefault=" + (k?.GetValue("") as string ?? Missing));
            File.WriteAllText(BackupFile, sb.ToString(), new UTF8Encoding(false));
        }

        using (var k = Registry.LocalMachine.CreateSubKey(HklmCmd))
        {
            k.SetValue("", $"\"{Exe}\"");
            k.DeleteValue("DelegateExecute", false);
        }
        using (var shell = Registry.CurrentUser.CreateSubKey(HkcuShell))
        {
            shell.SetValue("", VerbName);
            using var verb = shell.CreateSubKey(VerbName);
            verb.SetValue("", "用 ReFileBrowser 打开");
            verb.SetValue("Icon", $"\"{Exe}\"");
            using var cmd = verb.CreateSubKey("command");
            cmd.SetValue("", $"\"{Exe}\" \"%1\"");
        }
        RegisterVerbs();
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        return 0;
    }

    // 别的程序用 ShellExecute("open"/"explore", 文件夹) 打开时不看默认动作，而是直接找这两个动词。
    // 文件夹的关联链是 Directory/Drive 先于 Folder，所以在 HKCU 的 Directory、Drive 下定义就能盖过系统的 Folder\shell\open。
    // 只动文件系统文件夹和盘符，控制面板这类虚拟文件夹仍由系统打开。
    private static readonly string[] VerbKeys =
    {
        @"Software\Classes\Directory\shell\open", @"Software\Classes\Directory\shell\explore",
        @"Software\Classes\Drive\shell\open", @"Software\Classes\Drive\shell\explore",
    };

    private static void RegisterVerbs()
    {
        foreach (var path in VerbKeys)
        {
            using var verb = Registry.CurrentUser.CreateSubKey(path);
            verb.SetValue("", path.EndsWith("explore") ? "浏览(&X)" : "打开(&O)");
            verb.SetValue("ReFileBrowser", "1"); // 标记：恢复时只删带标记的键
            verb.SetValue("MultiSelectModel", "Document");
            using var cmd = verb.CreateSubKey("command");
            cmd.SetValue("", $"\"{Exe}\" \"%1\"");
        }
    }

    private static void UnregisterVerbs()
    {
        foreach (var path in VerbKeys)
        {
            using (var verb = Registry.CurrentUser.OpenSubKey(path))
                if (verb?.GetValue("ReFileBrowser") == null) continue;
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }

    /// <summary>在管理员进程里执行（ReFileBrowser.exe --unregister），按备份原样恢复。</summary>
    public static int Unregister()
    {
        var backup = new Dictionary<string, string>();
        if (File.Exists(BackupFile))
            foreach (var line in File.ReadAllLines(BackupFile))
            {
                int i = line.IndexOf('=');
                if (i > 0) backup[line[..i]] = line[(i + 1)..];
            }
        // 没有备份时恢复成 Windows 原始值
        string def = backup.GetValueOrDefault("HKLM_Default", Missing);
        string del = backup.GetValueOrDefault("HKLM_DelegateExecute", "{11dbb47c-a525-400b-9e80-a54615a090c0}");
        string hkcu = backup.GetValueOrDefault("HKCU_ShellDefault", Missing);

        using (var k = Registry.LocalMachine.CreateSubKey(HklmCmd))
        {
            if (def == Missing) k.DeleteValue("", false); else k.SetValue("", def);
            if (del == Missing) k.DeleteValue("DelegateExecute", false); else k.SetValue("DelegateExecute", del);
        }
        using (var shell = Registry.CurrentUser.CreateSubKey(HkcuShell))
        {
            shell.DeleteSubKeyTree(VerbName, false);
            if (hkcu == Missing) shell.DeleteValue("", false); else shell.SetValue("", hkcu);
        }
        UnregisterVerbs();
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        return 0;
    }

    public static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(VerbName) is string s && s.Contains("ReFileBrowser.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(VerbName, $"\"{Exe}\" --background");
        else k.DeleteValue(VerbName, false);
    }

    public static void CreateDesktopShortcut()
    {
        string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ReFileBrowser.lnk");
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) return;
        dynamic shell = Activator.CreateInstance(type);
        try
        {
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = Exe;
            sc.WorkingDirectory = Path.GetDirectoryName(Exe);
            sc.IconLocation = Exe + ",0";
            sc.Description = "ReFileBrowser 文件管理";
            sc.Save();
            Marshal.FinalReleaseComObject(sc);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
