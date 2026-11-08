using System.Runtime.InteropServices;
using System.Text;

namespace ReFileBrowser;

// 所有 Win32 / Shell COM 声明集中在这里。接口方法顺序必须和 shobjidl_core.h 的 vtable 一致。

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential)]
public struct FOLDERSETTINGS { public int ViewMode; public uint fFlags; }

[StructLayout(LayoutKind.Sequential)]
public struct NCCALCSIZE_PARAMS
{
    public RECT rgrc0, rgrc1, rgrc2;
    public IntPtr lppos;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct COPYDATASTRUCT
{
    public IntPtr dwData;
    public int cbData;
    public IntPtr lpData;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct SHFILEINFO
{
    public IntPtr hIcon;
    public int iIcon;
    public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MENUITEMINFO
{
    public int cbSize;
    public uint fMask;
    public uint fType;
    public uint fState;
    public uint wID;
    public IntPtr hSubMenu;
    public IntPtr hbmpChecked;
    public IntPtr hbmpUnchecked;
    public IntPtr dwItemData;
    public string dwTypeData;
    public uint cch;
    public IntPtr hbmpItem;
}

[StructLayout(LayoutKind.Sequential)]
public struct CMINVOKECOMMANDINFOEX
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public IntPtr lpVerb;
    public IntPtr lpParameters;
    public IntPtr lpDirectory;
    public int nShow;
    public uint dwHotKey;
    public IntPtr hIcon;
    public IntPtr lpTitle;
    public IntPtr lpVerbW;
    public IntPtr lpParametersW;
    public IntPtr lpDirectoryW;
    public IntPtr lpTitleW;
    public POINT ptInvoke;
}

// SHELLSTATE 的位域用两个 int 表示：Flags1 含 fShowAllObjects(bit0)/fShowExtensions(bit1)，
// Flags2 含 fAutoCheckSelect(bit3)。
[StructLayout(LayoutKind.Sequential)]
public struct SHELLSTATE
{
    public uint Flags1;
    public uint dwWin95Unused;
    public uint uWin95Unused;
    public int lParamSort;
    public int iSortDirection;
    public uint version;
    public uint uNotUsed;
    public uint Flags2;
}

[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFOHEADER
{
    public int biSize;
    public int biWidth;
    public int biHeight;
    public short biPlanes;
    public short biBitCount;
    public int biCompression;
    public int biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public int biClrUsed;
    public int biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
public struct TRACKMOUSEEVENT
{
    public int cbSize;
    public uint dwFlags;
    public IntPtr hwndTrack;
    public uint dwHoverTime;
}

[ComImport, Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowser
{
    [PreserveSig] int Initialize(IntPtr hwndParent, ref RECT prc, IntPtr pfs);
    [PreserveSig] int Destroy();
    [PreserveSig] int SetRect(IntPtr phdwp, RECT rcBrowser);
    [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string pszPropertyBag);
    [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string pszEmptyText);
    [PreserveSig] int SetFolderSettings(ref FOLDERSETTINGS pfs);
    [PreserveSig] int Advise(IntPtr psbe, out uint pdwCookie);
    [PreserveSig] int Unadvise(uint dwCookie);
    [PreserveSig] int SetOptions(uint dwFlag);
    [PreserveSig] int GetOptions(out uint pdwFlag);
    [PreserveSig] int BrowseToIDList(IntPtr pidl, uint uFlags);
    [PreserveSig] int BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint uFlags);
    [PreserveSig] int FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, int dwFlags);
    [PreserveSig] int RemoveAll();
    [PreserveSig] int GetCurrentView(ref Guid riid, out IntPtr ppv);
}

[ComImport, Guid("361bbdc7-e6ee-4e13-be58-58e2240c810f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowserEvents
{
    [PreserveSig] int OnNavigationPending(IntPtr pidlFolder);
    [PreserveSig] int OnViewCreated(IntPtr psv);
    [PreserveSig] int OnNavigationComplete(IntPtr pidlFolder);
    [PreserveSig] int OnNavigationFailed(IntPtr pidlFolder);
}

[ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleServiceProvider
{
    [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
}

/// <summary>
/// ExplorerBrowser 通过 QueryService(SID_SExplorerBrowserFrame = IID_ICommDlgBrowser) 向宿主要这个接口。
/// OnDefaultCommand 在双击/回车时先问宿主：返回 S_OK 表示宿主已处理，S_FALSE 走默认动作。
/// </summary>
[ComImport, Guid("000214F1-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICommDlgBrowser
{
    [PreserveSig] int OnDefaultCommand(IntPtr ppshv);
    [PreserveSig] int OnStateChange(IntPtr ppshv, uint uChange);
    [PreserveSig] int IncludeObject(IntPtr ppshv, IntPtr pidl);
}

[ComImport, Guid("e07010ec-bc17-44c0-97b0-46c7c95b9edc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerPaneVisibility
{
    [PreserveSig] int GetPaneState(ref Guid explorerPane, out uint peps);
}

[ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IObjectWithSite
{
    [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);
    [PreserveSig] int GetSite(ref Guid riid, out IntPtr ppvSite);
}

[ComImport, Guid("68284faa-6a48-11d0-8c78-00c04fd918b4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInputObject
{
    [PreserveSig] int UIActivateIO(int fActivate, IntPtr pMsg);
    [PreserveSig] int HasFocusIO();
    [PreserveSig] int TranslateAcceleratorIO(ref MSG pMsg);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellView
{
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
    [PreserveSig] int TranslateAccelerator(ref MSG pmsg);
    [PreserveSig] int EnableModeless(int fEnable);
    [PreserveSig] int UIActivate(uint uState);
    [PreserveSig] int Refresh();
    [PreserveSig] int CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
    [PreserveSig] int DestroyViewWindow();
    [PreserveSig] int GetCurrentInfo(out FOLDERSETTINGS pfs);
    [PreserveSig] int AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
    [PreserveSig] int SaveViewState();
    [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
    [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, out IntPtr ppv);
}

[ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFolderView2
{
    // IFolderView
    [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
    [PreserveSig] int SetCurrentViewMode(uint ViewMode);
    [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
    [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
    [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
    [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetSelectionMarkedItem(out int piItem);
    [PreserveSig] int GetFocusedItem(out int piItem);
    [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
    [PreserveSig] int GetSpacing(out POINT ppt);
    [PreserveSig] int GetDefaultSpacing(out POINT ppt);
    [PreserveSig] int GetAutoArrange();
    [PreserveSig] int SelectItem(int iItem, uint dwFlags);
    [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
    // IFolderView2
    [PreserveSig] int SetGroupBy(IntPtr key, int fAscending);
    [PreserveSig] int GetGroupBy(IntPtr pkey, out int pfAscending);
    [PreserveSig] int SetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr propvar);
    [PreserveSig] int GetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr ppropvar);
    [PreserveSig] int SetTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
    [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
    [PreserveSig] int SetText(int iType, [MarshalAs(UnmanagedType.LPWStr)] string pwszText);
    [PreserveSig] int SetCurrentFolderFlags(uint dwMask, uint dwFlags);
    [PreserveSig] int GetCurrentFolderFlags(out uint pdwFlags);
    [PreserveSig] int GetSortColumnCount(out int pcColumns);
    [PreserveSig] int SetSortColumns(IntPtr rgSortColumns, int cColumns);
    [PreserveSig] int GetSortColumns(IntPtr rgSortColumns, int cColumns);
    [PreserveSig] int GetItem(int iItem, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetVisibleItem(int iStart, int fPrevious, out int piItem);
    [PreserveSig] int GetSelectedItem(int iStart, out int piItem);
    [PreserveSig] int GetSelection(int fNoneImpliesFolder, out IntPtr ppsia);
    [PreserveSig] int GetSelectionState(IntPtr pidl, out uint pdwFlags);
    [PreserveSig] int InvokeVerbOnSelection([MarshalAs(UnmanagedType.LPStr)] string pszVerb);
    [PreserveSig] int SetViewModeAndIconSize(int uViewMode, int iImageSize);
    [PreserveSig] int GetViewModeAndIconSize(out int puViewMode, out int piImageSize);
    [PreserveSig] int SetGroupSubsetCount(uint cVisibleRows);
    [PreserveSig] int GetGroupSubsetCount(out uint pcVisibleRows);
    [PreserveSig] int SetRedraw(int fRedrawOn);
    [PreserveSig] int IsMoveInSameFolder();
    [PreserveSig] int DoRename();
}

[ComImport, Guid("BCFCE0A0-EC17-11d0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu3
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
}

[ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
}

[ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITaskbarList
{
    [PreserveSig] int HrInit();
    [PreserveSig] int AddTab(IntPtr hwnd);
    [PreserveSig] int DeleteTab(IntPtr hwnd);
    [PreserveSig] int ActivateTab(IntPtr hwnd);
    [PreserveSig] int SetActiveAlt(IntPtr hwnd);
}

public static class Taskbar
{
    private static ITaskbarList _tb;

    private static ITaskbarList Get()
    {
        if (_tb != null) return _tb;
        _tb = (ITaskbarList)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090")));
        _tb.HrInit();
        return _tb;
    }

    public static void Remove(IntPtr hwnd) { try { Get().DeleteTab(hwnd); } catch (COMException) { } }
    public static void Add(IntPtr hwnd) { try { Get().AddTab(hwnd); } catch (COMException) { } }
}

public static class Guids
{
    public static readonly Guid CLSID_ExplorerBrowser = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    public static readonly Guid IID_IShellView = new("000214E3-0000-0000-C000-000000000046");
    public static readonly Guid IID_IFolderView2 = new("1af3a467-214f-4298-908e-06b03e0b39f9");
    public static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    public static readonly Guid IID_IExplorerPaneVisibility = new("e07010ec-bc17-44c0-97b0-46c7c95b9edc");
    public static readonly Guid FOLDERID_ComputerFolder = new("0AC0837C-BBF8-452A-850D-79D08E667CA7");
    public static readonly Guid FOLDERID_Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    // EP_* 窗格
    public static readonly Guid EP_NavPane = new("cb316b22-25f7-42b8-8a09-540d23a43c2f");
    public static readonly Guid EP_Commands = new("d9745868-ca5f-4a76-91cd-f5a129fbb076");
    public static readonly Guid EP_Commands_Organize = new("72e81700-e3ec-4660-bf24-3c3b7b648806");
    public static readonly Guid EP_Commands_View = new("21f7c32d-eeaa-439b-bb51-37b96fd6a943");
    public static readonly Guid EP_DetailsPane = new("43abf98b-89b8-472d-b9ce-e69b8229f019");
    public static readonly Guid EP_PreviewPane = new("893c63d1-45c8-4d17-be19-223be71be365");
    public static readonly Guid EP_QueryPane = new("65bcde4f-4f07-4f27-83a7-1afca4df7ddd");
    public static readonly Guid EP_AdvQueryPane = new("b4e9db8b-34ba-4c39-b5cc-16a1bd2c411c");
    public static readonly Guid EP_StatusBar = new("65fe56ce-5cfe-4bc4-ad8a-7ae3fe7e8f7c");
    public static readonly Guid EP_Ribbon = new("D27524A8-C9F2-4834-A106-DF8889FD4F37");
}

public static class Win32
{
    public const int WM_SETFOCUS = 0x0007;
    public const int WM_NCCALCSIZE = 0x0083;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCMOUSEMOVE = 0x00A0;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCLBUTTONDBLCLK = 0x00A3;
    public const int WM_NCMOUSELEAVE = 0x02A2;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_CHAR = 0x0102;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSCHAR = 0x0106;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_INITMENUPOPUP = 0x0117;
    public const int WM_MENUCHAR = 0x0120;
    public const int WM_DRAWITEM = 0x002B;
    public const int WM_MEASUREITEM = 0x002C;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;
    public const int WM_APPCOMMAND = 0x0319;
    public const int WM_COPYDATA = 0x004A;
    public const int WM_DPICHANGED = 0x02E0;

    public const int HTCLIENT = 1, HTCAPTION = 2, HTMAXBUTTON = 9, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;

    public const int SM_CXFRAME = 32, SM_CYFRAME = 33, SM_CXPADDEDBORDER = 92, SM_CXSMICON = 49;

    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetFocus();
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool InsertMenuItem(IntPtr hmenu, uint item, bool fByPosition, ref MENUITEMINFO lpmi);
    [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr hMenu);
    [DllImport("user32.dll")] public static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetMenuString(IntPtr hMenu, uint uIDItem, StringBuilder lpString, int cchMax, uint flags);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint dwProcessId);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);
    [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hWnd, IntPtr rc, IntPtr rgn, uint flags);
    [DllImport("user32.dll")] public static extern bool GetAsyncKeyState(int vKey);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_LAYERED = 0x80000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
    public const int WM_CLOSE = 0x0010, WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232, WM_MOVING = 0x0216, WM_SIZING = 0x0214;

    public const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100, RDW_FRAME = 0x400;
    public const int WM_SYSCOMMAND = 0x0112, SC_DRAGMOVE = 0xF012;
    public const int DWMWA_CLOAK = 13;

    /// <summary>阻塞到 DWM 下一次合成完成（约一帧），用来确保刚画的内容已经上屏。</summary>
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();

    /// <summary>DWM 隐藏：窗口照常处于显示状态（布局、绘制都正常进行），只是不合成到屏幕上。</summary>
    public static void Cloak(IntPtr hwnd, bool on)
    {
        int v = on ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref v, 4);
    }

    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr ho);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("shell32.dll")] public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);
    [DllImport("shell32.dll")] public static extern int SHGetKnownFolderIDList(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppidl);
    [DllImport("shell32.dll")] public static extern IntPtr ILClone(IntPtr pidl);
    [DllImport("shell32.dll")] public static extern void ILFree(IntPtr pidl);
    [DllImport("shell32.dll")] public static extern bool ILRemoveLastID(IntPtr pidl);
    [DllImport("shell32.dll")] public static extern bool ILIsEqual(IntPtr pidl1, IntPtr pidl2);
    [DllImport("shell32.dll")] public static extern bool ILIsParent(IntPtr pidl1, IntPtr pidl2, bool fImmediate);
    [DllImport("shell32.dll")] public static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")] public static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);
    public const uint SHGFI_ATTRIBUTES = 0x800;
    public const uint SFGAO_FOLDER = 0x20000000, SFGAO_STREAM = 0x00400000, SFGAO_LINK = 0x00010000;
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attr, ref SHFILEINFO psfi, uint cb, uint flags);
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfoPath(string path, uint attr, ref SHFILEINFO psfi, uint cb, uint flags);
    [DllImport("shell32.dll")] public static extern void SHGetSetSettings(ref SHELLSTATE lpss, uint dwMask, bool bSet);

    public const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_PIDL = 0x8, SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint SIGDN_NORMALDISPLAY = 0, SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000, SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000, SIGDN_FILESYSPATH = 0x80058000;

    public static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static Rectangle WorkAreaAtCursor()
    {
        GetCursorPos(out var pt);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(pt, 2), ref mi);
        return Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
    }
}
