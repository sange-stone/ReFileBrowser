using System.Runtime.InteropServices;

namespace ReFileBrowser;

/// <summary>全局窗格开关（导航窗格/预览/详细信息），所有标签共用。</summary>
public static class PaneSettings
{
    public static bool NavPane = true;
    public static bool PreviewPane = false;
    public static bool DetailsPane = false;
}

/// <summary>
/// COM 回调单独放在一个普通对象上，避免把 WinForms Control 自己的 OLE 接口暴露给 ExplorerBrowser。
/// </summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class BrowserSink : IExplorerBrowserEvents, IOleServiceProvider, IExplorerPaneVisibility, ICommDlgBrowser
{
    private const int S_OK = 0, S_FALSE = 1, E_NOINTERFACE = unchecked((int)0x80004002);
    private static readonly Guid IID_ICommDlgBrowser = new("000214F1-0000-0000-C000-000000000046");
    private const uint EPS_DEFAULT_ON = 1, EPS_DEFAULT_OFF = 2, EPS_FORCE = 0x20000;

    private readonly BrowserHost _host;
    public BrowserSink(BrowserHost host) { _host = host; }

    // 返回失败码就取消这次导航（IExplorerBrowserEvents::OnNavigationPending 的约定）
    public int OnNavigationPending(IntPtr pidlFolder) => _host.HandleNavigationPending(pidlFolder) ? S_OK : unchecked((int)0x80004004) /* E_ABORT */;
    public int OnViewCreated(IntPtr psv) => S_OK;
    public int OnNavigationComplete(IntPtr pidlFolder) { _host.HandleNavigated(pidlFolder); return S_OK; }
    public int OnNavigationFailed(IntPtr pidlFolder) { _host.HandleNavigateFailed(pidlFolder); return S_OK; }

    public int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject)
    {
        if (guidService == Guids.IID_IExplorerPaneVisibility)
        {
            ppvObject = Marshal.GetComInterfaceForObject(this, typeof(IExplorerPaneVisibility));
            return S_OK;
        }
        // SID_SExplorerBrowserFrame 就是 IID_ICommDlgBrowser
        if (guidService == IID_ICommDlgBrowser && riid == IID_ICommDlgBrowser)
        {
            ppvObject = Marshal.GetComInterfaceForObject(this, typeof(ICommDlgBrowser));
            return S_OK;
        }
        ppvObject = IntPtr.Zero;
        return E_NOINTERFACE;
    }

    // 双击/回车：单个文件夹就在当前标签里打开。没有这一步的话，接管后的默认动作会去启动
    // ReFileBrowser.exe "路径"，结果变成新标签。文件、压缩包、多选仍交给系统默认处理。
    public int OnDefaultCommand(IntPtr ppshv) => _host.HandleDefaultCommand() ? S_OK : S_FALSE;
    public int OnStateChange(IntPtr ppshv, uint uChange) => S_OK;
    public int IncludeObject(IntPtr ppshv, IntPtr pidl) => S_OK;

    public int GetPaneState(ref Guid ep, out uint peps)
    {
        static uint On(bool on) => (on ? EPS_DEFAULT_ON : EPS_DEFAULT_OFF) | EPS_FORCE;
        if (ep == Guids.EP_NavPane) peps = On(PaneSettings.NavPane);
        else if (ep == Guids.EP_StatusBar) peps = On(true);
        else if (ep == Guids.EP_PreviewPane) peps = On(PaneSettings.PreviewPane);
        else if (ep == Guids.EP_DetailsPane) peps = On(PaneSettings.DetailsPane);
        else if (ep == Guids.EP_Commands || ep == Guids.EP_Commands_Organize || ep == Guids.EP_Commands_View
              || ep == Guids.EP_QueryPane || ep == Guids.EP_AdvQueryPane || ep == Guids.EP_Ribbon)
            peps = On(false);
        else peps = 0;
        return S_OK;
    }
}

/// <summary>一个标签页的内容：系统 ExplorerBrowser + 自己维护的前进/后退历史。</summary>
public sealed class BrowserHost : Control
{
    private const uint EBO_SHOWFRAMES = 0x2, EBO_ALWAYSNAVIGATE = 0x4, EBO_NOTRAVELLOG = 0x8;
    private const uint SBSP_ABSOLUTE = 0;
    private const uint SVSI_SELECT = 0x1, SVSI_DESELECTOTHERS = 0x4, SVSI_ENSUREVISIBLE = 0x8, SVSI_FOCUSED = 0x10;

    private IExplorerBrowser _eb;
    private BrowserSink _sink;
    private uint _cookie;
    private Pidl _pendingStart;

    private readonly List<Pidl> _history = new();
    private int _index = -1;
    private int _pendingHistoryIndex = -1;
    private Pidl _selectAfterNav;

    /// <summary>首次导航完成后要把导航树滚到的位置（见 NavScroll）；-1 表示不处理。</summary>
    public int PendingNavScroll { get; set; } = -1;

    /// <summary>是否已经完成过至少一次导航（内容已经画好，可以显示出来了）。</summary>
    public bool FirstNavigated { get; private set; }

    public Pidl Current { get; private set; }
    public string Title { get; private set; } = "";
    public Bitmap Icon { get; private set; }

    public event EventHandler Navigated;

    public BrowserHost(Pidl start)
    {
        _pendingStart = start;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _history.Count - 1;
    public bool CanGoUp => Current != null && !Current.IsDesktop;
    public IReadOnlyList<Pidl> History => _history;
    public int HistoryIndex => _index;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_eb != null) return;
        _sink = new BrowserSink(this);
        _eb = (IExplorerBrowser)Activator.CreateInstance(Type.GetTypeFromCLSID(Guids.CLSID_ExplorerBrowser));
        ((IObjectWithSite)_eb).SetSite(_sink);

        var rc = BrowserRect();
        // FWF_NOHEADERINALLVIEWS：只在详细信息视图显示列头（360 和资源管理器都是这样）
        var fs = new FOLDERSETTINGS { ViewMode = -1, fFlags = 0x01000000 };
        IntPtr pfs = Marshal.AllocHGlobal(Marshal.SizeOf<FOLDERSETTINGS>());
        try
        {
            Marshal.StructureToPtr(fs, pfs, false);
            Marshal.ThrowExceptionForHR(_eb.Initialize(Handle, ref rc, pfs));
        }
        finally { Marshal.FreeHGlobal(pfs); }

        _eb.SetOptions(EBO_SHOWFRAMES | EBO_NOTRAVELLOG | EBO_ALWAYSNAVIGATE);
        // 不设 property bag 的话 ExplorerBrowser 不保存每个文件夹的查看方式，每次打开都是默认视图。
        // 保存发生在离开文件夹 / 销毁视图时；用户改视图后另外调 SaveView 立即写入。
        _eb.SetPropertyBag("ReFileBrowser");
        IntPtr pEvents = Marshal.GetComInterfaceForObject(_sink, typeof(IExplorerBrowserEvents));
        try { _eb.Advise(pEvents, out _cookie); }
        finally { Marshal.Release(pEvents); }

        var start = _pendingStart ?? Pidl.ThisPC();
        _pendingStart = null;
        Navigate(start);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _eb?.SetRect(IntPtr.Zero, BrowserRect());
    }

    // ExplorerBrowser 四周自带 1px 的 #646464 边框；向外扩 1px 让宿主控件裁掉，外面由窗体画 360 的浅色边
    private RECT BrowserRect() => new(-1, -1, Math.Max(1, Width) + 1, Math.Max(1, Height) + 1);

    // 所有按键都交给内部的原生控件，不让 WinForms 拿方向键/Tab 做焦点切换
    protected override bool IsInputKey(Keys keyData) => true;
    protected override bool IsInputChar(char charCode) => true;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        FocusView();
    }

    public void FocusView()
    {
        var view = GetShellView();
        if (view == null) return;
        try { if (view.GetWindow(out var h) == 0 && h != IntPtr.Zero) Win32.SetFocus(h); }
        finally { Marshal.ReleaseComObject(view); }
    }

    public bool HasFocusInside()
    {
        var f = Win32.GetFocus();
        return f != IntPtr.Zero && IsHandleCreated && (f == Handle || Win32.IsChild(Handle, f));
    }

    /// <summary>把键盘消息交给 shell 视图的加速键表（Del、F2、Ctrl+C/V/X/A、F5 等）。</summary>
    public bool TranslateAccelerator(ref MSG msg)
    {
        if (_eb is not IInputObject io) return false;
        if (io.HasFocusIO() != 0) return false;
        return io.TranslateAcceleratorIO(ref msg) == 0;
    }

    /// <summary>调试：导航完成 800ms 后记录这个位置能不能在 ExplorerBrowser 里正常显示（有没有视图、几项、视图窗口类）。</summary>
    private void ProbeViewLater(string parsing)
    {
        var t = new System.Windows.Forms.Timer { Interval = 800 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            if (IsDisposed) return;
            string viewClass = "-", items = "-";
            var view = GetShellView();
            if (view != null)
            {
                try { if (view.GetWindow(out var h) == 0 && h != IntPtr.Zero) viewClass = Win32.ClassOf(h); }
                finally { Marshal.ReleaseComObject(view); }
            }
            var fv = GetFolderView();
            if (fv != null)
            {
                try { if (fv.ItemCount(2 /* SVGIO_ALLVIEW */, out int n) >= 0) items = n.ToString(); }
                catch (COMException) { }
                finally { Marshal.ReleaseComObject(fv); }
            }
            Log.W($"PROBE '{Title}' parsing={parsing} view={viewClass} items={items} fs={(Current?.FileSystemPath != null)}");
        };
        t.Start();
    }

    /// <summary>即将导航（任何来源：地址栏、导航树、双击、前进后退）。</summary>
    public event EventHandler NavigationPending;

    /// <summary>
    /// 这个标签还没显示过任何位置，要打开的却交给了资源管理器（例如新标签直接导航到控制面板）。
    /// 主窗口收到后关掉这个空标签。
    /// </summary>
    public event EventHandler HandedOff;

    internal bool HandleNavigationPending(IntPtr pidlFolder)
    {
        var target = Pidl.CloneOf(pidlFolder);
        if (target != null && !ShellRules.Hostable(target))
        {
            // 控制面板等只能由资源管理器显示的位置：取消本次导航，交给资源管理器
            _pendingHistoryIndex = -1;
            _selectAfterNav = null;
            BeginInvoke(() => HandOff(target));
            return false;
        }
        target?.Dispose();
        NavigationPending?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void HandOff(Pidl target)
    {
        try { ShellRules.OpenInExplorer(target, FindForm()?.Handle ?? IntPtr.Zero); }
        finally { target.Dispose(); }
        if (Current == null) HandedOff?.Invoke(this, EventArgs.Empty);
    }

    internal bool HandleDefaultCommand()
    {
        const uint SVGIO_SELECTION = 1;
        if (Current == null) return false;
        var fv = GetFolderView();
        if (fv == null) return false;
        IntPtr child = IntPtr.Zero, full = IntPtr.Zero;
        try
        {
            if (fv.ItemCount(SVGIO_SELECTION, out int n) < 0 || n != 1) return false;
            if (fv.GetSelectedItem(-1, out int idx) < 0 || idx < 0) return false;
            if (fv.Item(idx, out child) < 0 || child == IntPtr.Zero) return false;
            // Item 返回相对当前文件夹的子 ID，拼上当前文件夹就是绝对 ID
            full = Win32.ILCombine(Current.Ptr, child);
            if (full == IntPtr.Zero) return false;
            var fi = new SHFILEINFO { dwAttributes = Win32.SFGAO_FOLDER | Win32.SFGAO_STREAM };
            Win32.SHGetFileInfoPidl(full, 0, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), Win32.SHGFI_PIDL | Win32.SHGFI_ATTRIBUTES);
            bool folder = (fi.dwAttributes & Win32.SFGAO_FOLDER) != 0 && (fi.dwAttributes & Win32.SFGAO_STREAM) == 0;
            if (!folder) return false;
            var target = Pidl.CloneOf(full);
            BeginInvoke(() => Navigate(target)); // 不在 DefView 的回调里直接切换视图
            return true;
        }
        catch (COMException) { return false; }
        finally
        {
            if (full != IntPtr.Zero) Win32.ILFree(full);
            if (child != IntPtr.Zero) Marshal.FreeCoTaskMem(child);
            Marshal.ReleaseComObject(fv);
        }
    }

    /// <summary>下一次导航完成后选中 item（当 item 就在那个文件夹里时）。</summary>
    public void SelectAfterNavigate(Pidl item) => _selectAfterNav = item;

    /// <summary>打开 item 所在的文件夹并选中它（explorer.exe /select 的效果）。</summary>
    public void NavigateAndSelect(Pidl item)
    {
        var parent = item?.Parent();
        if (parent == null) { Navigate(item); return; }
        if (_eb == null) { _pendingStart = parent; _selectAfterNav = item; return; }
        _selectAfterNav = item;
        _pendingHistoryIndex = -1;
        _eb.BrowseToIDList(parent.Ptr, SBSP_ABSOLUTE);
        parent.Dispose();
    }

    public void Navigate(Pidl pidl)
    {
        if (pidl == null || _eb == null) { if (_eb == null) _pendingStart = pidl; return; }
        _pendingHistoryIndex = -1;
        _eb.BrowseToIDList(pidl.Ptr, SBSP_ABSOLUTE);
    }

    public void Navigate(string path)
    {
        var p = Pidl.Parse(path);
        if (p != null) Navigate(p);
    }

    public void GoBack() => GoToHistory(_index - 1);
    public void GoForward() => GoToHistory(_index + 1);

    public void GoToHistory(int i)
    {
        if (_eb == null || i < 0 || i >= _history.Count || i == _index) return;
        var from = Current?.Clone();
        _pendingHistoryIndex = i;
        _selectAfterNav = from;
        _eb.BrowseToIDList(_history[i].Ptr, SBSP_ABSOLUTE);
    }

    public void GoUp()
    {
        var parent = Current?.Parent();
        if (parent == null) return;
        _selectAfterNav = Current.Clone();
        Navigate(parent);
    }

    public void Refresh2()
    {
        var view = GetShellView();
        if (view == null) return;
        try { view.Refresh(); } finally { Marshal.ReleaseComObject(view); }
    }

    /// <summary>窗格开关变化后重新导航当前文件夹，让 ExplorerBrowser 重新询问 GetPaneState。</summary>
    public void ReapplyPanes()
    {
        if (Current == null || _eb == null) return;
        _pendingHistoryIndex = _index;
        _eb.BrowseToIDList(Current.Ptr, SBSP_ABSOLUTE);
    }

    internal void HandleNavigated(IntPtr pidlFolder)
    {
        var pidl = Pidl.CloneOf(pidlFolder);
        if (pidl == null) return;
        Current = pidl;
        Title = pidl.DisplayName;
        Icon = ShellIcons.Small(pidl);

        if (_pendingHistoryIndex >= 0)
        {
            _index = _pendingHistoryIndex;
        }
        else if (_index < 0 || !_history[_index].SameAs(pidl))
        {
            for (int i = _history.Count - 1; i > _index; i--) { _history[i].Dispose(); _history.RemoveAt(i); }
            _history.Add(pidl.Clone());
            _index = _history.Count - 1;
            if (_history.Count > 100) { _history[0].Dispose(); _history.RemoveAt(0); _index--; }
        }
        _pendingHistoryIndex = -1;


        var select = _selectAfterNav;
        _selectAfterNav = null;
        if (select != null && pidl.IsImmediateParentOf(select))
            BeginInvoke(() => SelectChild(select));

        Log.W($"navigated host={GetHashCode():X} first={!FirstNavigated} visible={Visible} '{Title}'");
        if (Log.Enabled) ProbeViewLater(pidl.ParsingName);
        FirstNavigated = true;
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    internal void HandleNavigateFailed(IntPtr pidlFolder)
    {
        _pendingHistoryIndex = -1;
        _selectAfterNav = null;
        // 兜底：虚拟位置在 ExplorerBrowser 里打不开（资源管理器专用的页面），交给资源管理器。
        // 文件系统路径打不开（没权限、盘没插）资源管理器也一样打不开，就不转了。
        var target = Pidl.CloneOf(pidlFolder);
        if (target == null) return;
        if (target.FileSystemPath != null) { target.Dispose(); return; }
        Log.W($"navigation failed in ExplorerBrowser: {target.ParsingName}");
        BeginInvoke(() => HandOff(target));
    }

    private void SelectChild(Pidl child)
    {
        var view = GetShellView();
        if (view == null) return;
        try
        {
            view.SelectItem(Win32.ILFindLastID(child.Ptr),
                SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_FOCUSED);
        }
        finally { Marshal.ReleaseComObject(view); child.Dispose(); }
    }

    public IShellView GetShellView()
    {
        if (_eb == null) return null;
        var iid = Guids.IID_IShellView;
        if (_eb.GetCurrentView(ref iid, out var p) < 0 || p == IntPtr.Zero) return null;
        try { return (IShellView)Marshal.GetObjectForIUnknown(p); }
        finally { Marshal.Release(p); }
    }

    [DllImport("user32.dll")] private static extern int GetScrollPos(IntPtr hWnd, int nBar);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr l);
    private delegate bool EnumChildProc(IntPtr h, IntPtr l);

    private IntPtr NavTree()
    {
        if (!IsHandleCreated) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(Handle, (h, _) =>
        {
            if (Win32.ClassOf(h) != "SysTreeView32") return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private const int TVM_GETNEXTITEM = 0x110A, TVM_SELECTITEM = 0x110B;
    private const int TVGN_ROOT = 0, TVGN_FIRSTVISIBLE = 5, TVGN_NEXTVISIBLE = 6;

    /// <summary>
    /// 导航窗格（左侧树）最上面那一行是第几个可见项（从 0 数）；没有导航窗格时为 -1。
    /// 用行序号而不是滚动条位置，因为两个标签的树是各自独立的控件，只有行的排列相同。
    /// </summary>
    public int NavScroll
    {
        get
        {
            var t = NavTree();
            if (t == IntPtr.Zero) return -1;
            IntPtr first = Win32.SendMessage(t, TVM_GETNEXTITEM, (IntPtr)TVGN_FIRSTVISIBLE, IntPtr.Zero);
            if (first == IntPtr.Zero) return -1;
            IntPtr item = Win32.SendMessage(t, TVM_GETNEXTITEM, (IntPtr)TVGN_ROOT, IntPtr.Zero);
            for (int n = 0; item != IntPtr.Zero && n < 2000; n++)
            {
                if (item == first) return n;
                item = Win32.SendMessage(t, TVM_GETNEXTITEM, (IntPtr)TVGN_NEXTVISIBLE, item);
            }
            return -1;
        }
        set
        {
            var t = NavTree();
            if (t == IntPtr.Zero || value < 0) return;
            IntPtr item = Win32.SendMessage(t, TVM_GETNEXTITEM, (IntPtr)TVGN_ROOT, IntPtr.Zero);
            for (int n = 0; n < value && item != IntPtr.Zero; n++)
            {
                IntPtr next = Win32.SendMessage(t, TVM_GETNEXTITEM, (IntPtr)TVGN_NEXTVISIBLE, item);
                if (next == IntPtr.Zero) break;
                item = next;
            }
            if (item != IntPtr.Zero) Win32.SendMessage(t, TVM_SELECTITEM, (IntPtr)TVGN_FIRSTVISIBLE, item);
        }
    }

    /// <summary>
    /// 用户在某个标签里改了查看方式并已保存。由 AppHost 接住，同步给开着同一文件夹的其他标签：
    /// 否则那些标签离开文件夹/被关闭时，ExplorerBrowser 会把它们手里的旧视图写回去，覆盖掉新的。
    /// </summary>
    public static event Action<BrowserHost> UserViewChanged;

    /// <summary>把当前文件夹的查看方式立即写进 property bag（否则要等离开这个文件夹才写）。</summary>
    public void SaveView(bool userChange = false)
    {
        var view = GetShellView();
        if (view == null) return;
        try { view.SaveViewState(); }
        catch (COMException) { }
        finally { Marshal.ReleaseComObject(view); }
        var state = GetViewState();
        _savedViewKey = state?.Key;
        if (userChange && state != null) UserViewChanged?.Invoke(this);
    }

    private string _savedViewKey;
    private Pidl _savedViewFolder;

    /// <summary>视图模式、图标大小、排序列、分组。排序和分组保留原始字节（SORTCOLUMN[] / PROPERTYKEY），原样写回。</summary>
    public sealed class ViewState
    {
        public int Mode, Size;
        public byte[] Sort;      // SORTCOLUMN 数组，每项 24 字节：PROPERTYKEY(20) + SORTDIRECTION(4)
        public byte[] Group;     // PROPERTYKEY，20 字节
        public int GroupAsc;
        public string Key => $"{Mode},{Size}|s{Convert.ToHexString(Sort ?? Array.Empty<byte>())}|g{Convert.ToHexString(Group ?? Array.Empty<byte>())}{GroupAsc}";
    }

    private const int SortColumnSize = 24, PropKeySize = 20;

    public ViewState GetViewState()
    {
        var fv = GetFolderView();
        if (fv == null) return null;
        IntPtr buf = IntPtr.Zero;
        try
        {
            var s = new ViewState();
            if (fv.GetViewModeAndIconSize(out s.Mode, out s.Size) < 0) return null;
            if (fv.GetSortColumnCount(out int n) >= 0 && n > 0)
            {
                n = Math.Min(n, 8);
                buf = Marshal.AllocHGlobal(SortColumnSize * n);
                if (fv.GetSortColumns(buf, n) >= 0)
                {
                    s.Sort = new byte[SortColumnSize * n];
                    Marshal.Copy(buf, s.Sort, 0, s.Sort.Length);
                }
                Marshal.FreeHGlobal(buf); buf = IntPtr.Zero;
            }
            buf = Marshal.AllocHGlobal(PropKeySize);
            if (fv.GetGroupBy(buf, out s.GroupAsc) >= 0)
            {
                s.Group = new byte[PropKeySize];
                Marshal.Copy(buf, s.Group, 0, PropKeySize);
            }
            return s;
        }
        catch (COMException) { return null; }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            Marshal.ReleaseComObject(fv);
        }
    }

    public void ApplyViewState(ViewState s)
    {
        if (s == null) return;
        var fv = GetFolderView();
        if (fv == null) return;
        IntPtr buf = IntPtr.Zero;
        try
        {
            fv.SetViewModeAndIconSize(s.Mode, s.Size);
            if (s.Sort is { Length: > 0 })
            {
                buf = Marshal.AllocHGlobal(s.Sort.Length);
                Marshal.Copy(s.Sort, 0, buf, s.Sort.Length);
                fv.SetSortColumns(buf, s.Sort.Length / SortColumnSize);
                Marshal.FreeHGlobal(buf); buf = IntPtr.Zero;
            }
            if (s.Group != null)
            {
                buf = Marshal.AllocHGlobal(PropKeySize);
                Marshal.Copy(s.Group, 0, buf, PropKeySize);
                fv.SetGroupBy(buf, s.GroupAsc);
            }
        }
        catch (COMException) { }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            Marshal.ReleaseComObject(fv);
        }
        _savedViewKey = GetViewState()?.Key; // 同步过来的不算本标签的「用户修改」
        _savedViewFolder?.Dispose();
        _savedViewFolder = Current?.Clone();
    }

    /// <summary>
    /// 由主窗口定时调用：当前文件夹的视图和上次保存时不一样了就立即保存。
    /// 系统右键菜单「查看」、点列头排序、分组这些改动不经过我们的代码，只能靠比较发现。
    /// </summary>
    public void SaveViewIfChanged()
    {
        if (Current == null) return;
        string key = GetViewState()?.Key;
        if (key == null) return;
        // 刚进一个新文件夹时只记下读到的状态，不当成「用户改了」
        if (_savedViewFolder == null || !_savedViewFolder.SameAs(Current))
        {
            _savedViewFolder?.Dispose();
            _savedViewFolder = Current.Clone();
            _savedViewKey = key;
            return;
        }
        if (key == _savedViewKey) return;
        Log.W($"view changed in '{Title}', saving");
        SaveView(userChange: true);
    }

    /// <summary>读当前视图模式和图标尺寸；没有视图时返回 false。</summary>
    public bool TryGetView(out int mode, out int size)
    {
        mode = -1; size = 0;
        var fv = GetFolderView();
        if (fv == null) return false;
        try { return fv.GetViewModeAndIconSize(out mode, out size) >= 0; }
        catch (COMException) { return false; }
        finally { Marshal.ReleaseComObject(fv); }
    }

    public IFolderView2 GetFolderView()
    {
        if (_eb == null) return null;
        var iid = Guids.IID_IFolderView2;
        if (_eb.GetCurrentView(ref iid, out var p) < 0 || p == IntPtr.Zero) return null;
        try { return (IFolderView2)Marshal.GetObjectForIUnknown(p); }
        finally { Marshal.Release(p); }
    }

    protected override void Dispose(bool disposing)
    {
        // 先把 _eb 置空再拆：Destroy 内部会处理消息，期间可能再次进入这里（连按 Ctrl+W、窗体关闭），
        // 对同一个 ExplorerBrowser 调两次 Destroy 会在 coreclr 里访问冲突。
        var eb = _eb;
        _eb = null;
        if (eb != null)
        {
            try
            {
                if (_cookie != 0) eb.Unadvise(_cookie);
                _cookie = 0;
                ((IObjectWithSite)eb).SetSite(null);
                eb.Destroy();
            }
            catch (COMException) { }
            catch (InvalidComObjectException) { }
            Marshal.ReleaseComObject(eb);
        }
        if (disposing)
        {
            foreach (var h in _history) h.Dispose();
            _history.Clear();
            Current?.Dispose();
            Current = null;
        }
        base.Dispose(disposing);
    }
}
