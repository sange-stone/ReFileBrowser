using System.Runtime.InteropServices;

namespace ReFileBrowser;

/// <summary>
/// 常驻进程：管理所有窗口，接收其他 ReFileBrowser.exe 转发来的命令行，并始终备好一个预热的隐藏窗口。
/// 关掉最后一个窗口进程也不退出（下次 Win+E 直接显示预热窗口），只有菜单「退出」才结束。
/// </summary>
public sealed class AppHost : ApplicationContext, IMessageFilter
{
    public const string MsgWindowTitle = "ReFileBrowser.Msg.{7C1E2A61-3B0F-4C5E-9A3D-5F17C0E0B6A1}";

    public static AppHost Instance { get; private set; }

    private readonly List<MainForm> _windows = new();
    private readonly MsgWindow _msg;
    private readonly System.Windows.Forms.Timer _spareTimer = new() { Interval = 1500 };
    private MainForm _spare;

    public AppHost(string[] args)
    {
        Instance = this;
        Application.AddMessageFilter(this);
        BrowserHost.UserViewChanged += SyncView;
        _msg = new MsgWindow(this);
        _spareTimer.Tick += (_, _) => { _spareTimer.Stop(); EnsureSpare(); };
        HandleCommand(args);
    }

    public void HandleCommand(string[] args)
    {
        bool background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        int sel = Array.FindIndex(args, a => a.Equals("--select", StringComparison.OrdinalIgnoreCase));
        if (sel >= 0 && sel + 1 < args.Length)
        {
            // explorer.exe /select,<项> 转过来的：打开所在文件夹并选中它
            var item = Pidl.Parse(Environment.ExpandEnvironmentVariables(args[sel + 1].Trim('"')));
            if (item != null) { if (!ShowInExisting(null, item)) OpenWindow(null, item); return; }
        }
        string target = args.FirstOrDefault(a => !a.StartsWith("--") && !a.StartsWith("/"));
        if (background && target == null)
        {
            EnsureSpare();
            return;
        }
        var t = ResolveTarget(target);
        if (!ShellRules.Hostable(t))
        {
            // 控制面板等：别人通过默认动作交给我们的，直接转给资源管理器，不开窗口也不开标签
            ShellRules.OpenInExplorer(t, IntPtr.Zero);
            t.Dispose();
            if (background) EnsureSpare();
            return;
        }
        if (!ShowInExisting(t, null)) OpenWindow(t);
    }

    /// <summary>最近激活过的窗口，外部打开请求（Win+E、双击文件夹……）优先交给它。</summary>
    public MainForm LastActive { get; set; }

    /// <summary>
    /// 外部打开请求：已有窗口时不再新建。没有目标（Win+E）就把最近用的窗口调到前台，
    /// 有目标就在那个窗口里新开一个标签。
    /// </summary>
    private bool ShowInExisting(Pidl target, Pidl select)
    {
        var f = LastActive;
        if (f == null || f.IsDisposed || f.IsClosing || !_windows.Contains(f)) f = _windows.LastOrDefault(w => !w.IsDisposed && !w.IsClosing);
        if (f == null) return false;
        if (select != null) f.AddTab(select.Parent(), true).SelectAfterNavigate(select);
        else if (target != null) f.AddTab(target, true);
        BringToFront(f);
        return true;
    }

    private static void BringToFront(MainForm f)
    {
        if (f.WindowState == FormWindowState.Minimized) Win32.ShowWindow(f.Handle, 9 /* SW_RESTORE */);
        f.Show();
        f.Activate();
        Win32.SetForegroundWindow(f.Handle);
    }

    private static Pidl ResolveTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        var p = Pidl.Parse(target);
        // 传进来的是文件时打开它所在的文件夹
        if (p != null && File.Exists(target) && !Directory.Exists(target))
        {
            var parent = p.Parent();
            p.Dispose();
            return parent;
        }
        return p;
    }

    /// <summary>打开一个新窗口。优先用预热好的隐藏窗口，这样几乎是瞬间显示。</summary>
    public void OpenWindow(Pidl target, Pidl select = null)
    {
        MainForm f;
        if (_spare != null && !_spare.IsDisposed)
        {
            f = _spare;
            _spare = null;
            if (select != null) f.Active?.NavigateAndSelect(select);
            else if (target != null) f.Active?.Navigate(target);
            f.PlaceOnCursorMonitor();
            Unveil(f);
        }
        else
        {
            f = CreateWindow(select != null ? null : target);
            if (select != null) f.Active?.NavigateAndSelect(select);
        }

        _windows.Add(f);
        f.Show();
        if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
        f.Activate();
        Win32.SetForegroundWindow(f.Handle);
        _spareTimer.Stop();
        _spareTimer.Start();
    }

    private MainForm CreateWindow(Pidl target)
    {
        var f = new MainForm(target);
        f.FormClosed += (_, _) => _windows.Remove(f);
        return f;
    }

    /// <summary>
    /// 预建一个窗口：真的 Show 出来（这样 ExplorerBrowser 按实际大小排版、绘制），但用 DWM cloak 不合成到屏幕，
    /// 并从任务栏去掉按钮。只建句柄不显示的话，隐藏的 ExplorerBrowser 不排版，显示出来会缺块、导航树也会滚位。
    /// </summary>
    private void EnsureSpare()
    {
        if (_spare != null && !_spare.IsDisposed) return;
        var f = CreateWindow(null);
        f.IsSpare = true;
        Win32.Cloak(f.Handle, true);
        f.Show();
        Taskbar.Remove(f.Handle);
        _spare = f;
    }

    private void Unveil(MainForm f)
    {
        f.IsSpare = false;
        Taskbar.Add(f.Handle);
        Win32.Cloak(f.Handle, false);
    }

    public IReadOnlyList<MainForm> Windows => _windows;

    /// <summary>某个标签的视图被用户改了：开着同一文件夹的其他标签（含预建窗口）立即跟着改。</summary>
    private void SyncView(BrowserHost source)
    {
        var folder = source.Current;
        if (folder == null) return;
        var state = source.GetViewState();
        if (state == null) return;
        var all = _windows.SelectMany(w => w.Tabs).ToList();
        if (_spare != null && !_spare.IsDisposed) all.AddRange(_spare.Tabs);
        foreach (var t in all)
            if (t != source && !t.IsDisposed && t.Current != null && t.Current.SameAs(folder))
                t.ApplyViewState(state);
    }

    /// <summary>把从别的窗口拖出来的标签放进一个新窗口。</summary>
    public MainForm WindowForTab(BrowserHost host, Size size)
    {
        var f = new MainForm(host, size);
        f.FormClosed += (_, _) => _windows.Remove(f);
        _windows.Add(f);
        return f;
    }

    public void ForEachTab(Action<BrowserHost> act)
    {
        foreach (var w in _windows.ToList())
            foreach (var t in w.Tabs.ToList()) act(t);
    }

    public void Exit()
    {
        foreach (var w in _windows.ToList()) w.Close();
        _spare?.Close();
        _spare?.Dispose();
        _msg.DestroyHandle();
        ExitThread();
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg < Win32.WM_KEYDOWN || (m.Msg > Win32.WM_SYSCHAR && m.Msg != Win32.WM_MOUSEWHEEL
            && m.Msg != Win32.WM_XBUTTONDOWN && m.Msg != Win32.WM_XBUTTONUP))
            return false;
        var ctl = Control.FromChildHandle(m.HWnd);
        return ctl?.FindForm() is MainForm f && f.PreFilter(ref m);
    }

    /// <summary>只接收消息的隐藏窗口，第二个进程通过 WM_COPYDATA 把命令行发过来。</summary>
    private sealed class MsgWindow : NativeWindow
    {
        private readonly AppHost _host;

        public MsgWindow(AppHost host)
        {
            _host = host;
            CreateHandle(new CreateParams { Caption = MsgWindowTitle, Parent = (IntPtr)(-3) /* HWND_MESSAGE */ });
            Win32.ChangeWindowMessageFilterEx(Handle, Win32.WM_COPYDATA, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_COPYDATA)
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
                if (cds.dwData == (IntPtr)0x5346 && cds.lpData != IntPtr.Zero)
                {
                    string s = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2);
                    var parts = s.Split('\0');
                    string cwd = parts[0];
                    var args = parts.Skip(1).Where(a => a.Length > 0)
                        .Select(a => !a.StartsWith("-") && !a.StartsWith("/") && !a.StartsWith("::") && !Path.IsPathRooted(a) && !a.Contains(':') && Directory.Exists(Path.Combine(cwd, a)) ? Path.Combine(cwd, a) : a)
                        .ToArray();
                    // 先回复发送方，再异步开窗口，避免对方 SendMessage 等太久
                    m.Result = (IntPtr)1;
                    var ctx = SynchronizationContext.Current;
                    ctx?.Post(_ => _host.HandleCommand(args), null);
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }
}
