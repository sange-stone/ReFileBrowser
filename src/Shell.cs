using System.Runtime.InteropServices;

namespace ReFileBrowser;

/// <summary>持有一份绝对 PIDL（相对桌面），释放时 ILFree。</summary>
public sealed class Pidl : IDisposable
{
    public IntPtr Ptr { get; private set; }

    private Pidl(IntPtr p) { Ptr = p; }

    public static Pidl CloneOf(IntPtr p) => p == IntPtr.Zero ? null : new Pidl(Win32.ILClone(p));

    public static Pidl Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        int hr = Win32.SHParseDisplayName(name, IntPtr.Zero, out var p, 0, out _);
        return hr >= 0 && p != IntPtr.Zero ? new Pidl(p) : null;
    }

    public static Pidl Known(Guid folderId)
    {
        int hr = Win32.SHGetKnownFolderIDList(ref folderId, 0, IntPtr.Zero, out var p);
        return hr >= 0 && p != IntPtr.Zero ? new Pidl(p) : null;
    }

    public static Pidl ThisPC() => Known(Guids.FOLDERID_ComputerFolder);

    public Pidl Clone() => new(Win32.ILClone(Ptr));

    public bool IsDesktop => Marshal.ReadInt16(Ptr) == 0;

    /// <summary>父级；已经是桌面时返回 null。</summary>
    public Pidl Parent()
    {
        if (IsDesktop) return null;
        var c = Clone();
        Win32.ILRemoveLastID(c.Ptr);
        return c;
    }

    public bool SameAs(Pidl other) => other != null && Win32.ILIsEqual(Ptr, other.Ptr);

    public bool IsImmediateParentOf(Pidl child) => child != null && Win32.ILIsParent(Ptr, child.Ptr, true);

    public string Name(uint sigdn)
    {
        if (Win32.SHGetNameFromIDList(Ptr, sigdn, out var s) < 0 || s == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(s); }
        finally { Marshal.FreeCoTaskMem(s); }
    }

    public string DisplayName => Name(Win32.SIGDN_NORMALDISPLAY) ?? "";
    public string ParsingName => Name(Win32.SIGDN_DESKTOPABSOLUTEPARSING) ?? "";
    public string EditingName => Name(Win32.SIGDN_DESKTOPABSOLUTEEDITING) ?? ParsingName;
    public string FileSystemPath => Name(Win32.SIGDN_FILESYSPATH);

    public void Dispose()
    {
        if (Ptr != IntPtr.Zero) { Win32.ILFree(Ptr); Ptr = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }

    ~Pidl() { if (Ptr != IntPtr.Zero) Win32.ILFree(Ptr); }
}

/// <summary>
/// 哪些位置 ReFileBrowser 自己显示，哪些交还给系统资源管理器。
/// 控制面板里的页面（电源选项、用户帐户、网络和共享中心……）只能由资源管理器自己的窗口承载，
/// 放进 IExplorerBrowser 会导航失败或只剩空白（2026-11-09 用 tools\probe_targets.ps1 实测）。
/// 所以整个控制面板命名空间都交给资源管理器；其余系统位置（此电脑、回收站、网络、库、主文件夹、
/// 图库、应用、字体……）实测都能正常显示，照常接管。
/// </summary>
public static class ShellRules
{
    private static readonly string[] SystemOnly =
    {
        "::{26EE0668-A00A-44D7-9371-BEB064C98683}", // 控制面板（分类视图）
        "::{21EC2020-3AEA-1069-A2DD-08002B30309D}", // 所有控制面板项
    };

    public static bool Hostable(Pidl p)
    {
        if (p == null) return true;
        string n = p.ParsingName;
        foreach (var s in SystemOnly)
            if (n.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    /// <summary>
    /// 用资源管理器打开。显式用 "open" 动词：接管只改了默认动作、Directory/Drive 的 open，
    /// 虚拟文件夹的 open 仍是系统的 Folder\shell\open（资源管理器），不会再绕回 ReFileBrowser。
    /// </summary>
    public static void OpenInExplorer(Pidl p, IntPtr owner)
    {
        if (p == null) return;
        Log.W($"hand to Explorer: {p.ParsingName}");
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = 0x00000004 /* SEE_MASK_IDLIST */ | 0x00000100 /* SEE_MASK_ASYNCOK */,
            hwnd = owner,
            lpVerb = "open",
            lpIDList = p.Ptr,
            nShow = 1,
        };
        ShellExecuteEx(ref info);
    }
}

public static class ShellIcons
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap Small(Pidl pidl)
    {
        if (pidl == null) return null;
        string key = pidl.ParsingName;
        if (Cache.TryGetValue(key, out var bmp)) return bmp;
        var fi = new SHFILEINFO();
        Win32.SHGetFileInfoPidl(pidl.Ptr, 0, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(),
            Win32.SHGFI_PIDL | Win32.SHGFI_ICON | Win32.SHGFI_SMALLICON);
        bmp = FromHIcon(fi.hIcon);
        // 只缓存固定位置（盘符、虚拟文件夹），普通文件夹图标可能被 desktop.ini 改掉但也很少变，一并缓存
        if (bmp != null && Cache.Count < 512) Cache[key] = bmp;
        return bmp;
    }

    private static readonly Dictionary<string, Bitmap> PathCache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap SmallForPath(string path)
    {
        if (PathCache.TryGetValue(path, out var cached)) return cached;
        Bitmap bmp;
        using (var p = Pidl.Parse(path))
        {
            if (p != null) bmp = Small(p);
            else
            {
                // 路径暂时不存在（如移动硬盘没插）时用通用文件夹图标
                var fi = new SHFILEINFO();
                Win32.SHGetFileInfoPath(path, 0x10, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(),
                    Win32.SHGFI_ICON | Win32.SHGFI_SMALLICON | Win32.SHGFI_USEFILEATTRIBUTES);
                bmp = FromHIcon(fi.hIcon);
            }
        }
        PathCache[path] = bmp;
        return bmp;
    }

    private static Bitmap FromHIcon(IntPtr h)
    {
        if (h == IntPtr.Zero) return null;
        try { using var ico = Icon.FromHandle(h); return ico.ToBitmap(); }
        finally { Win32.DestroyIcon(h); }
    }
}
