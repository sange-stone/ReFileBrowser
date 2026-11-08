using System.Runtime.InteropServices;

namespace ReFileBrowser;

/// <summary>
/// 任务栏分组。没有显式 AppUserModelID 时 Windows 按 exe 路径分组，固定的快捷方式指向旧路径就会和运行中的窗口分成两个图标。
/// 这里给进程设固定 ID，并把任务栏里指向 ReFileBrowser.exe 的固定项改成当前 exe、写上同一个 ID。
/// </summary>
public static class AppId
{
    public const string Id = "ReFileBrowser.App";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    public static void Apply() => SetCurrentProcessExplicitAppUserModelID(Id);

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(IntPtr pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments(IntPtr pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile(out IntPtr ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT pv);

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static PROPERTYKEY PKEY_AppUserModel_ID = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    /// <summary>把任务栏固定项里的 ReFileBrowser 快捷方式指向当前 exe 并写入 AppUserModelID。返回改动的个数。</summary>
    public static int FixPinnedShortcuts()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        if (!Directory.Exists(dir)) return 0;
        string exe = Environment.ProcessPath;
        int changed = 0;
        foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk"))
        {
            try { if (FixOne(lnk, exe)) changed++; }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { }
        }
        return changed;
    }

    private static bool FixOne(string lnk, string exe)
    {
        var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellLink));
        try
        {
            var pf = (IPersistFile)link;
            pf.Load(lnk, 2 /* STGM_READWRITE */);
            IntPtr buf = Marshal.AllocCoTaskMem(2 * 1024);
            string target;
            try { link.GetPath(buf, 1024, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */); target = Marshal.PtrToStringUni(buf); }
            finally { Marshal.FreeCoTaskMem(buf); }
            if (!string.Equals(Path.GetFileName(target), "ReFileBrowser.exe", StringComparison.OrdinalIgnoreCase)) return false;

            var store = (IPropertyStore)link;
            store.GetValue(ref PKEY_AppUserModel_ID, out var cur);
            string curId = cur.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(cur.p) : null;
            PropVariantClear(ref cur);
            bool samePath = string.Equals(target, exe, StringComparison.OrdinalIgnoreCase);
            if (samePath && curId == Id) return false;

            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe));
            link.SetIconLocation(exe, 0);
            var pv = new PROPVARIANT { vt = 31, p = Marshal.StringToCoTaskMemUni(Id) };
            try { store.SetValue(ref PKEY_AppUserModel_ID, ref pv); store.Commit(); }
            finally { PropVariantClear(ref pv); }
            pf.Save(lnk, true);
            return true;
        }
        finally { Marshal.ReleaseComObject(link); }
    }
}
