using System.Runtime.InteropServices;

namespace ReFileBrowser;

/// <summary>
/// 主窗口。系统标题栏用 WM_NCCALCSIZE 去掉，但保留系统边框，所以拖动缩放、阴影、Win11 贴靠布局照常工作。
/// 自上而下：TitleBar(32) / FavBar(30) / 线 / CmdBar(48) / 线 / NavBar(40) / 标签内容。
/// </summary>
public sealed partial class MainForm : Form
{
    private readonly TitleBar _title = new() { Dock = DockStyle.Top };
    private readonly FavBar _fav = new() { Dock = DockStyle.Top };
    private readonly Panel _line1 = new() { Dock = DockStyle.Top, BackColor = Theme.LineLight };
    private readonly CmdBar _cmd = new() { Dock = DockStyle.Top };
    private readonly Panel _line2 = new() { Dock = DockStyle.Top, BackColor = Theme.LineDark };
    private readonly NavBar _nav = new() { Dock = DockStyle.Top };
    private readonly Panel _line3 = new() { Dock = DockStyle.Top, BackColor = Theme.Frame };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 300 };
    private readonly Stack<string> _closedTabs = new();
    private bool _maxDown;

    public List<BrowserHost> Tabs => _title.Tabs;
    public BrowserHost Active => _title.Selected >= 0 && _title.Selected < Tabs.Count ? Tabs[_title.Selected] : null;

    /// <summary>预建窗口（已显示但被 cloak）：不抢焦点。</summary>
    public bool IsSpare { get; set; }
    protected override bool ShowWithoutActivation => IsSpare;

    public MainForm(Pidl start) : this()
    {
        AddTab(start ?? Pidl.ThisPC(), true);
        PlaceOnCursorMonitor();
    }

    /// <summary>接收从别的窗口拖出来的标签（BrowserHost 句柄和历史都保留）。</summary>
    public MainForm(BrowserHost moved, Size size) : this()
    {
        Size = size;
        AttachTab(moved, 0, true);
    }

    private MainForm()
    {
        Text = "ReFileBrowser";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Theme.Frame;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = false;
        ShowIcon = true;
        Icon = AppIcon.Get();

        SuspendLayout();
        Controls.Add(_content);
        Controls.Add(_line3);
        Controls.Add(_nav);
        Controls.Add(_line2);
        Controls.Add(_cmd);
        Controls.Add(_line1);
        Controls.Add(_fav);
        Controls.Add(_title);
        ApplyDpiSizes();
        ResumeLayout(true);

        WireTitle();
        WireNav();
        WireFav();
        _cmd.Clicked += OnCmd;
        _uiTimer.Tick += (_, _) => { UpdateCommandStates(); _shownTab?.SaveViewIfChanged(); };
    }

    private int S(int v) => Theme.S(v, DeviceDpi);

    private void ApplyDpiSizes()
    {
        _title.Height = S(32);
        _fav.Height = S(30);
        // 360 的 VerticalLayout inset="3,1,3,3"：白色内容四周留一圈 #DFE8F1 外框
        Padding = new Padding(S(3), S(1), S(3), S(3));
        _line1.Height = Math.Max(1, S(1));
        _cmd.Height = S(48);
        _line2.Height = Math.Max(1, S(1));
        _nav.Height = S(40);
        _line3.Height = Math.Max(1, S(1));
        MinimumSize = new Size(S(496), S(300));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiSizes();
    }

    /// <summary>按上次的窗口大小，放在鼠标所在显示器的中央。</summary>
    public void PlaceOnCursorMonitor()
    {
        var wa = Win32.WorkAreaAtCursor();
        int w = Math.Min(Store.GetInt("WindowWidth", 2440), wa.Width);
        int h = Math.Min(Store.GetInt("WindowHeight", 1537), wa.Height);
        Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
        if (Store.GetBool("Maximized", false)) WindowState = FormWindowState.Maximized;
    }

    /// <summary>预热：不显示窗口，但先建好句柄和 ExplorerBrowser，让 shell 扩展在后台加载完。</summary>
    public void Prewarm()
    {
        _ = Handle;
        PerformLayout();
        foreach (var t in Tabs) { _ = t.Handle; }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _uiTimer.Start();
        Active?.FocusView();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _uiTimer.Start();
        if (!IsSpare) AppHost.Instance.LastActive = this;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _uiTimer.Stop();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        bool max = WindowState == FormWindowState.Maximized;
        if (_title.Maximized != max) { _title.Maximized = max; _title.Invalidate(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
        {
            Store.Set("WindowWidth", Width);
            Store.Set("WindowHeight", Height);
        }
        if (WindowState != FormWindowState.Minimized) Store.Set("Maximized", WindowState == FormWindowState.Maximized);
        Store.SaveSettings();
        base.OnFormClosing(e);
        if (e.Cancel) return;
        IsClosing = true;
        _uiTimer.Stop();
        // 这里窗口句柄还在，足够让 ExplorerBrowser 正常拆除；等到 FormClosed 时子窗口已经被销毁了
        foreach (var t in Tabs.ToList()) { t.Navigated -= OnTabNavigated; t.NavigationPending -= OnTabNavigationPending; t.HandedOff -= OnTabHandedOff; t.Dispose(); }
        Tabs.Clear();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer.Dispose();
        _wheelTimer?.Dispose();
        base.OnFormClosed(e);
    }

    // ---------------- 窗口外壳 ----------------

    private int FrameY()
    {
        uint dpi = (uint)DeviceDpi;
        return Win32.GetSystemMetricsForDpi(Win32.SM_CYFRAME, dpi) + Win32.GetSystemMetricsForDpi(Win32.SM_CXPADDEDBORDER, dpi);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Win32.WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
            {
                var before = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
                base.WndProc(ref m);
                var after = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
                // 去掉标题栏：顶边还原到窗口顶端；最大化时窗口会超出屏幕一个边框宽，要加回来
                after.rgrc0.Top = before.rgrc0.Top + (IsZoomed() ? FrameY() : 0);
                Marshal.StructureToPtr(after, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }
            case Win32.WM_NCHITTEST:
            {
                base.WndProc(ref m);
                if ((int)m.Result != Win32.HTCLIENT) return;
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int band = FrameY();
                if (!IsZoomed() && p.Y < band)
                {
                    m.Result = (IntPtr)(p.X < band * 2 ? Win32.HTTOPLEFT : p.X > ClientSize.Width - band * 2 ? Win32.HTTOPRIGHT : Win32.HTTOP);
                    return;
                }
                if (_title.Bounds.Contains(p))
                {
                    int hit = _title.NcHit(new Point(p.X - _title.Left, p.Y - _title.Top));
                    if (hit != Win32.HTCLIENT) m.Result = (IntPtr)hit;
                }
                return;
            }
            case Win32.WM_NCMOUSEMOVE:
            {
                bool onMax = (int)m.WParam == Win32.HTMAXBUTTON;
                _title.SetNcMax(onMax, onMax && _maxDown);
                if (onMax)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = 0x2 | 0x10, hwndTrack = Handle };
                    Win32.TrackMouseEvent(ref tme);
                }
                break;
            }
            case Win32.WM_MOVING when _tabDragMove:
                OnMovingWithTab();
                break;
            case Win32.WM_NCMOUSELEAVE:
                _title.SetNcMax(false, false);
                _maxDown = false;
                break;
            case Win32.WM_NCLBUTTONDOWN when (int)m.WParam == Win32.HTMAXBUTTON:
            case Win32.WM_NCLBUTTONDBLCLK when (int)m.WParam == Win32.HTMAXBUTTON:
                _maxDown = true;
                _title.SetNcMax(true, true);
                m.Result = IntPtr.Zero;
                return;
            case Win32.WM_NCLBUTTONUP when (int)m.WParam == Win32.HTMAXBUTTON:
                if (_maxDown) ToggleMaximize();
                _maxDown = false;
                _title.SetNcMax(true, false);
                m.Result = IntPtr.Zero;
                return;
            case Win32.WM_INITMENUPOPUP:
            case Win32.WM_DRAWITEM:
            case Win32.WM_MEASUREITEM:
            case Win32.WM_MENUCHAR:
                if (_shellMenu != null && _shellMenu.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var r) == 0)
                {
                    m.Result = r;
                    return;
                }
                break;
        }
        base.WndProc(ref m);
    }

    private bool IsZoomed() => WindowState == FormWindowState.Maximized;

    private void ToggleMaximize() => WindowState = IsZoomed() ? FormWindowState.Normal : FormWindowState.Maximized;

    private void WireTitle()
    {
        _title.TabClicked += SelectTab;
        _title.TabCloseClicked += CloseTab;
        _title.TabMoved += (from, to) =>
        {
            var t = Tabs[from]; Tabs.RemoveAt(from); Tabs.Insert(to, t);
            _title.Selected = Tabs.IndexOf(Active == t ? t : Active);
            _title.Selected = to;
            _title.RefreshTabs();
        };
        _title.TabRightClicked += (i, p) => ShowTabMenu(i, _title.PointToScreen(p));
        _title.TabDetach += OnTabDetach;
        _title.ButtonClicked += (part, rect) =>
        {
            switch (part)
            {
                case TitleBar.Part.Add: AddTab(Pidl.ThisPC(), true); break;
                case TitleBar.Part.Count: ShowTabList(_title.RectangleToScreen(rect)); break;
                case TitleBar.Part.Menu: ShowMainMenu(_title.RectangleToScreen(rect)); break;
                case TitleBar.Part.Min: WindowState = FormWindowState.Minimized; break;
                case TitleBar.Part.Max: ToggleMaximize(); break;
                case TitleBar.Part.Close: Close(); break;
            }
        };
        _title.TopResizeBand = FrameY();
    }

    // ---------------- 标签页 ----------------

    public BrowserHost AddTab(Pidl pidl, bool activate, int index = -1)
    {
        var host = new BrowserHost(pidl) { Dock = DockStyle.Fill, Visible = false };
        // 新标签可能打开同一个文件夹：先把当前标签改过的查看方式写进去，新标签就能读到
        Active?.SaveViewIfChanged();
        AttachTab(host, index, activate);
        return host;
    }

    /// <summary>把一个 BrowserHost（新建的，或从别的窗口拖来的）放进本窗口的第 index 个标签位。</summary>
    public void AttachTab(BrowserHost host, int index, bool activate)
    {
        host.Navigated += OnTabNavigated;
        host.NavigationPending += OnTabNavigationPending;
        host.HandedOff += OnTabHandedOff;
        host.Dock = DockStyle.Fill;
        // 和 360 一样：新标签立刻切到最前面，正文区当场清空，新视图在眼前一步步画出来。
        // 只把左侧导航树对齐到旧标签的位置（首次导航完成时应用），免得它从顶部跳下来。
        if (activate && !host.FirstNavigated && _shownTab != null && !_shownTab.IsDisposed)
            host.PendingNavScroll = _shownTab.NavScroll;
        // 新建并激活、窗口已经显示着：先让标签栏和空白正文上屏，再在下一轮消息里建 ExplorerBrowser。
        // 建 ExplorerBrowser + 首次导航是同步的，要占住界面线程两百多毫秒；不先上屏的话，按下 Ctrl+T
        // 后画面一动不动，然后新标签和内容一下子全冒出来（360 是新标签和清空的正文立刻出现）。
        if (activate && !host.FirstNavigated && Tabs.Count > 0 && Visible && IsHandleCreated && !IsSpare)
        {
            if (index < 0 || index > Tabs.Count) index = Tabs.Count;
            if (index <= _title.Selected) _title.Selected++;
            Tabs.Insert(index, host);
            _title.Selected = index;
            if (_shownTab != null) _shownTab.Visible = false;   // 正文区露出白色底板
            _title.RefreshTabs();
            _title.Update();
            _content.Update();
            Win32.DwmFlush();                                   // 等这一帧真正合成到屏幕上
            BeginInvoke(() =>
            {
                if (host.IsDisposed || IsClosing || !Tabs.Contains(host)) return;
                host.Visible = true;
                _content.Controls.Add(host);
                int i = Tabs.IndexOf(host);
                if (_title.Selected == i) SelectTab(i);
                else { host.SendToBack(); _ = host.Handle; }    // 期间用户已经切到别的标签
            });
            return;
        }

        // 后台打开的新标签也要可见地压在最底下：隐藏的 ExplorerBrowser 不排版，切过去时导航树会跳。
        host.Visible = true;
        _content.Controls.Add(host);   // 已有句柄时 WinForms 用 SetParent 搬过来，ExplorerBrowser 不重建
        if (!activate) host.SendToBack();
        if (index < 0 || index > Tabs.Count) index = Tabs.Count;
        if (index <= _title.Selected) _title.Selected++;
        Tabs.Insert(index, host);

        if (activate || Tabs.Count == 1)
        {
            if (IsHandleCreated) _ = host.Handle;
            SelectTab(index);
            return;
        }
        _title.RefreshTabs();
        if (IsHandleCreated) _ = host.Handle; // 立刻创建句柄开始导航，标题/图标/内容才有东西
    }

    /// <summary>取出第 i 个标签（不销毁），用于拖到其他窗口。调用方保证本窗口还剩至少一个标签。</summary>
    private BrowserHost DetachTab(int i)
    {
        var t = Tabs[i];
        t.Navigated -= OnTabNavigated;
        t.NavigationPending -= OnTabNavigationPending; t.HandedOff -= OnTabHandedOff;
        Tabs.RemoveAt(i);
        _content.Controls.Remove(t);
        if (Tabs.Count > 0)
        {
            int sel = _title.Selected;
            if (i < sel || sel >= Tabs.Count) sel--;
            _title.Selected = -1;
            SelectTab(Math.Clamp(sel, 0, Tabs.Count - 1));
        }
        return t;
    }

    // ---------------- 拖出 / 拖回标签 ----------------

    private bool _tabDragMove;

    /// <summary>
    /// 标签被拖出标签栏：多标签时把它搬进一个新窗口，单标签时拖的就是本窗口。
    /// 之后由系统的窗口移动循环（SC_DRAGMOVE）跟随鼠标，松手时如果落在另一个窗口的标签栏上就并进去。
    /// </summary>
    private void OnTabDetach(int index, Point screen, Point grab)
    {
        if (Tabs.Count == 1 && WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;

        // 新窗口和本窗口在同一块屏、同一 DPI，第一个标签的位置也一样（TabRect(0)），
        // 所以直接用本窗口的几何算出新窗口位置，让按下时标签上的那一点正好在鼠标下。
        // 不能先放到屏幕外再挪：那里没有显示器，窗口会按 96 DPI 建，挪回来后偏移全错。
        var clientOrigin = PointToScreen(Point.Empty);
        var tab0 = _title.TabRect(0);
        var at = new Point(
            screen.X - (clientOrigin.X - Left) - _title.Left - tab0.Left - grab.X,
            screen.Y - (clientOrigin.Y - Top) - _title.Top - tab0.Top - grab.Y);

        MainForm win = this;
        if (Tabs.Count > 1)
        {
            var size = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
            var host = DetachTab(index);
            win = AppHost.Instance.WindowForTab(host, size);
            win.Location = at;
            win.Show();
        }
        else Location = at;
        win._dragGrab = grab;
        win.BeginInvoke(win.DragWindowByTab);
    }

    private Point _dragGrab;
    private MainForm _mergeTarget;

    /// <summary>
    /// 系统的窗口移动循环带着窗口跟随鼠标。一旦鼠标进入另一个窗口的标签栏（WM_MOVING 里检测），
    /// 就结束移动、把标签并进去，并让对方标签栏接着拖（不用松手）。
    /// </summary>
    private void DragWindowByTab()
    {
        if (Control.MouseButtons != MouseButtons.Left) return;
        Activate();
        _mergeTarget = null;
        _tabDragMove = true;
        Win32.ReleaseCapture();
        Win32.SendMessage(Handle, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_DRAGMOVE, IntPtr.Zero); // 移动结束才返回
        _tabDragMove = false;
        var target = _mergeTarget ?? FindMergeTarget();
        _mergeTarget = null;
        if (target != null) MergeInto(target);
    }

    private void OnMovingWithTab()
    {
        if (_mergeTarget != null) return;
        var target = FindMergeTarget();
        if (target == null) return;
        _mergeTarget = target;
        Win32.PostMessage(Handle, 0x001F /* WM_CANCELMODE */, IntPtr.Zero, IntPtr.Zero); // 结束移动循环
    }

    private void MergeInto(MainForm target)
    {
        int at = target._title.InsertIndexAt(target._title.PointToClient(Cursor.Position));
        var host = DetachTab(0);
        target.AttachTab(host, at, true);
        target.Activate();
        Hide();
        Close();
        // 鼠标还按着：由对方标签栏接管，继续横向排序，或再竖向拖出去
        if (Control.MouseButtons == MouseButtons.Left) target._title.BeginExternalDrag(at, _dragGrab);
    }

    private MainForm FindMergeTarget()
    {
        if (Tabs.Count != 1) return null;
        var p = Cursor.Position;
        foreach (var w in AppHost.Instance.Windows)
        {
            if (w == this || w.IsDisposed || !w.Visible || w.WindowState == FormWindowState.Minimized) continue;
            if (w._title.RectangleToScreen(w._title.ClientRectangle).Contains(p)) return w;
        }
        return null;
    }



    /// <summary>内容区当前真正显示在最前面的标签（延后激活期间它和 Active 不是同一个）。</summary>
    private BrowserHost _shownTab;

    public void SelectTab(int i)
    {
        if (i < 0 || i >= Tabs.Count) return;
        var target = Tabs[i];
        _title.Selected = i;
        if (_shownTab != null && _shownTab != target && !_shownTab.IsDisposed) _shownTab.SaveViewIfChanged();
        target.Visible = true;
        target.BringToFront();
        _shownTab = target;
        // 还没导航完的后台新标签先留着可见（压在底下排版），OnTabNavigated 里再隐藏
        foreach (var t in Tabs) if (t != target && t.FirstNavigated) t.Visible = false;
        _title.RefreshTabs();
        UpdateNav();
        if (Visible) target.FocusView();
    }

    /// <summary>窗口已经开始关闭（已隐藏，等拆完 ExplorerBrowser 再真正 Close），不要再拿它来显示。</summary>
    public bool IsClosing { get; private set; }

    public void CloseTab(int i)
    {
        if (IsClosing || i < 0 || i >= Tabs.Count) return;
        var t = Tabs[i];
        if (t.Current != null) _closedTabs.Push(t.Current.ParsingName);
        t.Navigated -= OnTabNavigated;
        t.NavigationPending -= OnTabNavigationPending; t.HandedOff -= OnTabHandedOff;
        Tabs.RemoveAt(i);
        if (Tabs.Count == 0)
        {
            // 先拆 ExplorerBrowser 再关窗口：父窗口句柄先没了的话，Destroy 会等它的后台线程超时，卡住很久
            IsClosing = true;
            Hide();
            BeginInvoke(() => { t.Dispose(); Close(); });
            return;
        }
        int sel = _title.Selected;
        if (i < sel || sel >= Tabs.Count) sel--;
        _title.Selected = -1;
        SelectTab(Math.Clamp(sel, 0, Tabs.Count - 1));
        // 不在按键处理里当场拆（Destroy 会处理消息，导致重入），放到消息循环里做
        t.Visible = false;
        BeginInvoke(t.Dispose);
    }

    private void ReopenClosedTab()
    {
        while (_closedTabs.Count > 0)
        {
            var p = Pidl.Parse(_closedTabs.Pop());
            if (p != null) { AddTab(p, true, _title.Selected + 1); return; }
        }
    }

    /// <summary>新标签一开始就要打开控制面板之类的位置，已交给资源管理器：这个空标签没用了，关掉。</summary>
    private void OnTabHandedOff(object sender, EventArgs e)
    {
        int i = Tabs.IndexOf((BrowserHost)sender);
        if (i >= 0) CloseTab(i);
    }

    /// <summary>
    /// 某个标签要打开别的文件夹前，把其他标签（包括别的窗口）当前的查看方式先写进去。
    /// ExplorerBrowser 只在离开文件夹时保存，另一个标签还停在那个文件夹里的话，新打开的就读不到刚改的视图。
    /// </summary>
    private static void OnTabNavigationPending(object sender, EventArgs e)
        => AppHost.Instance.ForEachTab(t => { if (t != sender) t.SaveViewIfChanged(); }); // 只存真正改过的，不拿旧视图覆盖

    private void OnTabNavigated(object sender, EventArgs e)
    {
        var host = (BrowserHost)sender;
        _title.RefreshTabs();
        if (host.PendingNavScroll >= 0)
        {
            // 新标签首次导航完成：导航树对齐到旧标签的位置。树的子项是异步展开的，稍后再对齐一次。
            int want = host.PendingNavScroll;
            host.PendingNavScroll = -1;
            BeginInvoke(() => { if (!host.IsDisposed) host.NavScroll = want; });
            var t = new System.Windows.Forms.Timer { Interval = 150 };
            t.Tick += (_, _) => { t.Dispose(); if (!host.IsDisposed && host.NavScroll != want) host.NavScroll = want; };
            t.Start();
        }
        if (host != Active && host.FirstNavigated && host.Visible)
        {
            host.Visible = false; // 后台标签排好版后再隐藏，切回来时导航树位置是对的
        }
        if (host != Active) return;
        UpdateNav();
        var fs = host.Current?.FileSystemPath;
        if (fs != null) Store.AddHistory(fs);
    }

    private void UpdateNav()
    {
        var a = Active;
        if (a == null) return;
        Text = string.IsNullOrEmpty(a.Title) ? "ReFileBrowser" : a.Title;
        if (a.Current != null) _nav.SetState(a.Current, a.CanGoBack, a.CanGoForward, a.CanGoUp);
        UpdateCommandStates();
    }

    // ---------------- 键盘与鼠标侧键 ----------------

    /// <summary>由 AppHost 的全局消息过滤器调用；返回 true 表示已处理。</summary>
    public bool PreFilter(ref Message m)
    {
        if (m.Msg == Win32.WM_XBUTTONUP)
        {
            int btn = (int)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            if (btn == 1) Active?.GoBack(); else if (btn == 2) Active?.GoForward();
            return true;
        }
        if (m.Msg == Win32.WM_XBUTTONDOWN) return true;

        if (m.Msg == Win32.WM_MOUSEWHEEL)
        {
            const int MK_CONTROL = 0x8;
            long wp = m.WParam.ToInt64();
            if ((wp & MK_CONTROL) == 0 || !IsInFileView(m.HWnd)) return false;
            _wheelAccum += (short)((wp >> 16) & 0xFFFF);
            while (Math.Abs(_wheelAccum) >= 120)
            {
                int dir = Math.Sign(_wheelAccum);
                _wheelAccum -= dir * 120;
                QueueViewStep(dir);
            }
            return true;
        }

        if (m.Msg != Win32.WM_KEYDOWN && m.Msg != Win32.WM_SYSKEYDOWN
            && m.Msg != Win32.WM_KEYUP && m.Msg != Win32.WM_SYSKEYUP
            && m.Msg != Win32.WM_CHAR && m.Msg != Win32.WM_SYSCHAR) return false;

        if (m.Msg == Win32.WM_KEYDOWN || m.Msg == Win32.WM_SYSKEYDOWN)
        {
            var key = (Keys)(int)m.WParam | ModifierKeys;
            if (HandleShortcut(key)) return true;
        }

        var a = Active;
        if (a != null && a.HasFocusInside())
        {
            var msg = new MSG { hwnd = m.HWnd, message = (uint)m.Msg, wParam = m.WParam, lParam = m.LParam };
            if (a.TranslateAccelerator(ref msg)) return true;
        }
        return false;
    }

    private int _wheelAccum;

    // 从小到大：(视图模式, 图标尺寸)。FVM_CONTENT=8 TILE=6 DETAILS=4 LIST=3 SMALLICON=2 ICON=1
    private static readonly (int mode, int size)[] WheelSteps =
    {
        (8, 32), (6, 48), (4, 16), (3, 16), (2, 16),
        (1, 32), (1, 40), (1, 48), (1, 64), (1, 80), (1, 96), (1, 128), (1, 160), (1, 192), (1, 256),
    };

    /// <summary>鼠标下的窗口是否在当前标签的文件列表里（导航树、地址栏不算）。</summary>
    private bool IsInFileView(IntPtr hwnd)
    {
        var a = Active;
        if (a == null || !a.IsHandleCreated || (hwnd != a.Handle && !Win32.IsChild(a.Handle, hwnd))) return false;
        for (IntPtr h = hwnd; h != IntPtr.Zero && h != a.Handle; h = Win32.GetAncestor(h, 1 /* GA_PARENT */))
            if (Win32.ClassOf(h) == "SHELLDLL_DefView") return true;
        return false;
    }

    // 快速滚动时不能每格都同步切换视图：每次切换都会重新排版，滚轮消息堆积时 WM_PAINT 一直轮不到，
    // 界面就只剩滚动条在变。这里只记下目标档位，由定时器最多 60 次/秒应用最新的一档并立即重绘。
    private int _wheelTarget = -1;
    private int _wheelApplied = -1;
    private long _wheelLastTick;
    private System.Windows.Forms.Timer _wheelTimer;

    private void QueueViewStep(int dir)
    {
        long now = Environment.TickCount64;
        // 停顿超过 0.5 秒就重新读取当前视图（可能已经被菜单或切换文件夹改掉）
        if (_wheelTarget < 0 || now - _wheelLastTick > 500)
        {
            _wheelTarget = CurrentWheelStep(dir);
            _wheelApplied = _wheelTarget;
        }
        _wheelLastTick = now;
        _wheelTarget = Math.Clamp(_wheelTarget + dir, 0, WheelSteps.Length - 1);
        if (_wheelTimer == null)
        {
            _wheelTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _wheelTimer.Tick += (_, _) => { _wheelTimer.Stop(); ApplyWheelStep(); };
        }
        if (!_wheelTimer.Enabled) _wheelTimer.Start();
    }

    /// <summary>当前视图在 WheelSteps 里的位置。图标尺寸落在两档之间时，取滚动反方向那一档，这样第一格只走到相邻档。</summary>
    private int CurrentWheelStep(int dir)
    {
        int mode = -1, size = 0;
        var fv = Active?.GetFolderView();
        if (fv != null)
        {
            try { fv.GetViewModeAndIconSize(out mode, out size); }
            catch (COMException) { }
            finally { Marshal.ReleaseComObject(fv); }
        }
        if (mode != 1)
        {
            int i = Array.FindIndex(WheelSteps, s => s.mode == mode);
            return i >= 0 ? i : 7; // 缩略图等其他模式按中等图标算
        }
        int below = 5, above = WheelSteps.Length - 1;
        for (int i = 5; i < WheelSteps.Length; i++)
        {
            if (WheelSteps[i].size <= size) below = i;
            if (WheelSteps[i].size >= size) { above = i; break; }
        }
        return dir > 0 ? below : above;
    }

    private void ApplyWheelStep()
    {
        if (_wheelTarget < 0 || _wheelTarget == _wheelApplied) return;
        var a = Active;
        var fv = a?.GetFolderView();
        if (fv == null) return;
        try
        {
            var (mode, size) = WheelSteps[_wheelTarget];
            fv.SetViewModeAndIconSize(mode, size);
            _wheelApplied = _wheelTarget;
        }
        catch (COMException) { }
        finally { Marshal.ReleaseComObject(fv); }
        Win32.RedrawWindow(a.Handle, IntPtr.Zero, IntPtr.Zero,
            Win32.RDW_INVALIDATE | Win32.RDW_ALLCHILDREN | Win32.RDW_UPDATENOW);
        a.SaveView(userChange: true); // 立即记住这个文件夹的查看方式
    }

    private bool HandleShortcut(Keys key)
    {
        var a = Active;
        bool inBrowser = a != null && a.HasFocusInside();
        switch (key)
        {
            case Keys.Control | Keys.T: AddTab(Pidl.ThisPC(), true, _title.Selected + 1); return true;
            case Keys.Control | Keys.W:
            case Keys.Control | Keys.F4: CloseTab(_title.Selected); return true;
            case Keys.Control | Keys.Shift | Keys.T: ReopenClosedTab(); return true;
            case Keys.Control | Keys.Tab: SelectTab((_title.Selected + 1) % Tabs.Count); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: SelectTab((_title.Selected - 1 + Tabs.Count) % Tabs.Count); return true;
            case Keys.Control | Keys.N: AppHost.Instance.OpenWindow(a?.Current?.Clone()); return true;
            case Keys.Alt | Keys.Left: a?.GoBack(); return true;
            case Keys.Alt | Keys.Right: a?.GoForward(); return true;
            case Keys.Alt | Keys.Up: a?.GoUp(); return true;
            case Keys.Alt | Keys.D:
            case Keys.Control | Keys.L:
            case Keys.F4: _nav.BeginEdit(); return true;
            case Keys.Control | Keys.F:
            case Keys.Control | Keys.E:
            case Keys.F3: _nav.FocusSearch(); return true;
            case Keys.Back:
                if (inBrowser && Win32.ClassOf(Win32.GetFocus()) != "Edit") { a.GoBack(); return true; }
                return false;
        }
        if ((key & Keys.Modifiers) == (Keys.Control | Keys.Shift) && inBrowser)
        {
            // 资源管理器的视图快捷键：Ctrl+Shift+1..8 = 超大图标/大图标/中图标/小图标/列表/详细信息/平铺/内容
            var k = key & Keys.KeyCode;
            if (k >= Keys.D1 && k <= Keys.D8)
            {
                var v = ViewModes[k - Keys.D1];
                WithFolderView(fv => fv.SetViewModeAndIconSize(v.mode, v.size));
                a.SaveView(userChange: true);
                return true;
            }
        }
        if ((key & Keys.Modifiers) == Keys.Control)
        {
            var k = key & Keys.KeyCode;
            if (k >= Keys.D1 && k <= Keys.D9)
            {
                int i = k == Keys.D9 ? Tabs.Count - 1 : k - Keys.D1;
                if (i < Tabs.Count) SelectTab(i);
                return true;
            }
        }
        return false;
    }
}

public static class AppIcon
{
    private static Icon _icon;

    /// <summary>360 文件夹的程序图标（assets\app.ico，含 16~256px），任务栏和 Alt+Tab 按 DPI 自己挑尺寸。</summary>
    public static Icon Get()
    {
        if (_icon != null) return _icon;
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
        try { _icon = File.Exists(path) ? new Icon(path) : Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
        catch (Exception ex) when (ex is IOException or ArgumentException) { _icon = SystemIcons.Application; }
        return _icon;
    }
}
