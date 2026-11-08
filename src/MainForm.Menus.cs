using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ReFileBrowser;

public sealed partial class MainForm
{
    private const uint SVGIO_BACKGROUND = 0, SVGIO_SELECTION = 1;
    private const uint TPM_RETURNCMD = 0x100, TPM_LEFTALIGN = 0, TPM_TOPALIGN = 0;
    private const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    private const uint SSF_SHOWALLOBJECTS = 0x1, SSF_SHOWEXTENSIONS = 0x2, SSF_AUTOCHECKSELECT = 0x00800000;

    private IContextMenu3 _shellMenu;
    private static readonly uint CF_SHELLIDLIST = RegisterClipboardFormat("Shell IDList Array");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);

    // ---------------- 通用弹出菜单 ----------------

    private ContextMenuStrip NewMenu()
    {
        var m = new ContextMenuStrip
        {
            Renderer = new FlatRenderer(),
            ShowImageMargin = true,
            ImageScalingSize = new Size(S(16), S(16)),
            Font = Theme.UiFont(DeviceDpi),
        };
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        return m;
    }

    private ToolStripMenuItem Item(ContextMenuStrip m, string text, Image img, Action click, bool check = false)
    {
        var it = new ToolStripMenuItem(text, img, (_, _) => click()) { Checked = check, Padding = new Padding(0, S(3), 0, S(3)) };
        m.Items.Add(it);
        return it;
    }

    private Image Ico(string name) => Theme.Img(name, DeviceDpi);

    private static void ShowBelow(ContextMenuStrip m, Rectangle screenRect) => m.Show(new Point(screenRect.Left, screenRect.Bottom));

    // ---------------- 工具栏 ----------------

    private void OnCmd(CmdBar.Cmd cmd, Rectangle rect)
    {
        var a = Active;
        if (a == null) return;
        var screen = _cmd.RectangleToScreen(rect);
        switch (cmd)
        {
            case CmdBar.Cmd.New: ShowNewMenu(screen); break;
            case CmdBar.Cmd.View: ShowViewMenu(screen); break;
            case CmdBar.Cmd.Copy: InvokeOnSelection("copy"); break;
            case CmdBar.Cmd.Cut: InvokeOnSelection("cut"); break;
            case CmdBar.Cmd.Delete: InvokeOnSelection("delete"); break;
            case CmdBar.Cmd.Paste: InvokeBackgroundVerb("paste"); break;
            case CmdBar.Cmd.Rename:
                WithFolderView(fv => fv.DoRename());
                break;
        }
    }

    private void WithFolderView(Action<IFolderView2> act)
    {
        var fv = Active?.GetFolderView();
        if (fv == null) return;
        try { Active.FocusView(); act(fv); }
        catch (COMException) { }
        finally { Marshal.ReleaseComObject(fv); }
    }

    private void InvokeOnSelection(string verb) => WithFolderView(fv => fv.InvokeVerbOnSelection(verb));

    private IContextMenu3 GetBackgroundMenu(out IContextMenu plain)
    {
        plain = null;
        var view = Active?.GetShellView();
        if (view == null) return null;
        try
        {
            var iid = Guids.IID_IContextMenu;
            if (view.GetItemObject(SVGIO_BACKGROUND, ref iid, out var p) < 0 || p == IntPtr.Zero) return null;
            try
            {
                var obj = Marshal.GetObjectForIUnknown(p);
                plain = obj as IContextMenu;
                return obj as IContextMenu3;
            }
            finally { Marshal.Release(p); }
        }
        finally { Marshal.ReleaseComObject(view); }
    }

    private void InvokeBackgroundVerb(string verb)
    {
        GetBackgroundMenu(out var cm);
        if (cm == null) return;
        IntPtr menu = Win32.CreatePopupMenu();
        IntPtr a = Marshal.StringToHGlobalAnsi(verb), w = Marshal.StringToHGlobalUni(verb);
        try
        {
            cm.QueryContextMenu(menu, 0, 1, 0x7FFF, 0);
            var ici = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(), fMask = CMIC_MASK_UNICODE,
                hwnd = Handle, lpVerb = a, lpVerbW = w, nShow = 1,
            };
            cm.InvokeCommand(ref ici);
        }
        catch (COMException) { }
        finally
        {
            Marshal.FreeHGlobal(a); Marshal.FreeHGlobal(w);
            Win32.DestroyMenu(menu);
            Marshal.ReleaseComObject(cm);
        }
    }

    /// <summary>弹出系统背景右键菜单里的「新建」子菜单（和资源管理器一样的列表，新建后自动进入重命名）。</summary>
    private void ShowNewMenu(Rectangle screen)
    {
        var cm3 = GetBackgroundMenu(out var cm);
        if (cm == null) return;
        IntPtr menu = Win32.CreatePopupMenu();
        try
        {
            cm.QueryContextMenu(menu, 0, 1, 0x7FFF, 0);
            IntPtr target = FindSubMenu(menu, "新建", "New") ;
            if (target == IntPtr.Zero) target = menu;
            _shellMenu = cm3;
            Win32.SetForegroundWindow(Handle);
            int id = Win32.TrackPopupMenuEx(target, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_TOPALIGN, screen.Left, screen.Bottom, Handle, IntPtr.Zero);
            _shellMenu = null;
            if (id > 0)
            {
                Active?.FocusView();
                var ici = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(), fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = Handle, lpVerb = (IntPtr)(id - 1), lpVerbW = (IntPtr)(id - 1), nShow = 1,
                    ptInvoke = new POINT { X = screen.Left, Y = screen.Bottom },
                };
                cm.InvokeCommand(ref ici);
            }
        }
        catch (COMException) { }
        finally
        {
            _shellMenu = null;
            Win32.DestroyMenu(menu);
            Marshal.ReleaseComObject(cm);
        }
    }

    private static IntPtr FindSubMenu(IntPtr menu, params string[] prefixes)
    {
        int n = Win32.GetMenuItemCount(menu);
        var sb = new StringBuilder(256);
        for (int i = 0; i < n; i++)
        {
            IntPtr sub = Win32.GetSubMenu(menu, i);
            if (sub == IntPtr.Zero) continue;
            sb.Clear();
            Win32.GetMenuString(menu, (uint)i, sb, sb.Capacity, 0x400 /* MF_BYPOSITION */);
            string t = sb.ToString().Replace("&", "");
            if (prefixes.Any(p => t.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return sub;
        }
        return IntPtr.Zero;
    }

    private static readonly (string text, string img, int mode, int size)[] ViewModes =
    {
        ("超大图标", "view_largeex", 1, 256),
        ("大图标", "view_large", 1, 96),
        ("中图标", "view_medium", 1, 48),
        ("小图标", "view_small", 2, 16),
        ("列表", "view_list", 3, 16),
        ("详细信息", "view_detail", 4, 16),
        ("平铺", "view_tile", 6, 48),
        ("内容", "view_content", 8, 32),
    };

    private void ShowViewMenu(Rectangle screen)
    {
        int curMode = -1, curSize = 0;
        WithFolderView(fv => fv.GetViewModeAndIconSize(out curMode, out curSize));
        int current = -1;
        for (int i = 0; i < ViewModes.Length; i++)
        {
            var v = ViewModes[i];
            if (v.mode != curMode) continue;
            if (curMode != 1) { current = i; break; }
            // 图标模式按尺寸归到最近的一档
            if (current < 0 || Math.Abs(v.size - curSize) < Math.Abs(ViewModes[current].size - curSize)) current = i;
        }

        var m = NewMenu();
        for (int i = 0; i < ViewModes.Length; i++)
        {
            var v = ViewModes[i];
            var it = Item(m, v.text, Ico(v.img), () => { WithFolderView(fv => fv.SetViewModeAndIconSize(v.mode, v.size)); Active?.SaveView(userChange: true); });
            if (i == current) it.Font = new Font(m.Font, FontStyle.Bold);
        }
        m.Items.Add(new ToolStripSeparator());
        var show = new ToolStripMenuItem("显示") { Padding = new Padding(0, S(3), 0, S(3)) };
        m.Items.Add(show);
        var ss = new SHELLSTATE();
        Win32.SHGetSetSettings(ref ss, SSF_SHOWALLOBJECTS | SSF_SHOWEXTENSIONS | SSF_AUTOCHECKSELECT, false);
        bool hidden = (ss.Flags1 & 1) != 0, ext = (ss.Flags1 & 2) != 0, check = (ss.Flags2 & 8) != 0;
        show.DropDown = SubMenu(
            ("导航窗格", "option_navigate", PaneSettings.NavPane, () => { PaneSettings.NavPane ^= true; PanesChanged(); }),
            ("详细信息窗格", "option_detail", PaneSettings.DetailsPane, () => { PaneSettings.DetailsPane ^= true; if (PaneSettings.DetailsPane) PaneSettings.PreviewPane = false; PanesChanged(); }),
            ("预览窗格", "option_preview", PaneSettings.PreviewPane, () => { PaneSettings.PreviewPane ^= true; if (PaneSettings.PreviewPane) PaneSettings.DetailsPane = false; PanesChanged(); }),
            ("项目复选框", "option_check", check, () => SetShellFlag(SSF_AUTOCHECKSELECT, !check)),
            ("文件扩展名", "option_extension", ext, () => SetShellFlag(SSF_SHOWEXTENSIONS, !ext)),
            ("隐藏的项目", "option_hidden", hidden, () => SetShellFlag(SSF_SHOWALLOBJECTS, !hidden)));
        ShowBelow(m, screen);
    }

    private ToolStripDropDownMenu SubMenu(params (string text, string img, bool check, Action click)[] items)
    {
        var dd = new ToolStripDropDownMenu
        {
            Renderer = new FlatRenderer(), ShowImageMargin = true, ShowCheckMargin = true,
            ImageScalingSize = new Size(S(16), S(16)), Font = Theme.UiFont(DeviceDpi),
        };
        foreach (var (text, img, check, click) in items)
            dd.Items.Add(new ToolStripMenuItem(text, Ico(img), (_, _) => click()) { Checked = check, Padding = new Padding(0, S(3), 0, S(3)) });
        return dd;
    }

    private void PanesChanged()
    {
        Store.SaveSettings();
        AppHost.Instance.ForEachTab(t => t.ReapplyPanes());
    }

    private void SetShellFlag(uint mask, bool on)
    {
        var ss = new SHELLSTATE();
        Win32.SHGetSetSettings(ref ss, mask, false);
        if (mask == SSF_SHOWALLOBJECTS) ss.Flags1 = on ? ss.Flags1 | 1 : ss.Flags1 & ~1u;
        else if (mask == SSF_SHOWEXTENSIONS) ss.Flags1 = on ? ss.Flags1 | 2 : ss.Flags1 & ~2u;
        else if (mask == SSF_AUTOCHECKSELECT) ss.Flags2 = on ? ss.Flags2 | 8 : ss.Flags2 & ~8u;
        Win32.SHGetSetSettings(ref ss, mask, true);
        AppHost.Instance.ForEachTab(t => t.Refresh2());
    }

    private void UpdateCommandStates()
    {
        var a = Active;
        if (a == null) return;
        int sel = 0;
        var fv = a.GetFolderView();
        if (fv != null)
        {
            try { fv.ItemCount(SVGIO_SELECTION, out sel); } catch (COMException) { }
            finally { Marshal.ReleaseComObject(fv); }
        }
        bool fs = a.Current?.FileSystemPath != null;
        _cmd.SetEnabled(CmdBar.Cmd.New, fs);
        _cmd.SetEnabled(CmdBar.Cmd.Copy, sel > 0);
        _cmd.SetEnabled(CmdBar.Cmd.Cut, sel > 0);
        _cmd.SetEnabled(CmdBar.Cmd.Rename, sel > 0);
        _cmd.SetEnabled(CmdBar.Cmd.Delete, sel > 0);
        _cmd.SetEnabled(CmdBar.Cmd.Paste, fs && (Win32.IsClipboardFormatAvailable(15) || Win32.IsClipboardFormatAvailable(CF_SHELLIDLIST)));
    }

    // ---------------- 地址栏 / 搜索 ----------------

    private void WireNav()
    {
        _nav.Back += () => Active?.GoBack();
        _nav.Forward += () => Active?.GoForward();
        _nav.Up += () => Active?.GoUp();
        _nav.RefreshClicked += () => Active?.Refresh2();
        _nav.NavigateTo += p => Active?.Navigate(p.Clone());
        _nav.MiddleNavigate += p => AddTab(p.Clone(), false, _title.Selected + 1);
        _nav.AddressEntered += NavigateAddress;
        _nav.SearchEntered += Search;
        _nav.EditCancelled += () => Active?.FocusView();
        _nav.HistoryMenu += r => ShowHistoryMenu(_nav.RectangleToScreen(r));
        _nav.DropMenu += r => ShowAddressHistory(_nav.RectangleToScreen(r));
        _nav.SubfolderMenu += (p, r) => ShowSubfolders(p, _nav.RectangleToScreen(r));
        _nav.OverflowMenu += (list, r) =>
        {
            var m = NewMenu();
            foreach (var p in Enumerable.Reverse(list)) { var c = p; Item(m, c.DisplayName, ShellIcons.Small(c), () => Active?.Navigate(c.Clone())); }
            ShowBelow(m, _nav.RectangleToScreen(r));
        };
    }

    private void NavigateAddress(string text)
    {
        text = text.Trim().Trim('"');
        if (text.Length == 0) { Active?.FocusView(); return; }
        string expanded = Environment.ExpandEnvironmentVariables(text);
        if (File.Exists(expanded) && !Directory.Exists(expanded))
        {
            Launch(expanded, null);
            return;
        }
        var p = Pidl.Parse(expanded);
        if (p != null) { Active?.Navigate(p); Active?.FocusView(); return; }
        // 和资源管理器一样：不是路径就当命令运行（cmd、notepad、网址等）
        Launch(text, Active?.Current?.FileSystemPath);
    }

    private void Launch(string target, string workDir)
    {
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (workDir != null) psi.WorkingDirectory = workDir;
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"找不到“{target}”。请检查拼写并重试。", "ReFileBrowser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Search(string q)
    {
        var a = Active;
        if (a?.Current == null) return;
        q = q.Trim();
        if (q.Length == 0) return;
        // 在搜索结果里再搜，仍然以原始文件夹为范围
        string loc = a.Current.ParsingName;
        if (loc.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase) && _lastSearchLocation != null) loc = _lastSearchLocation;
        _lastSearchLocation = loc;
        string uri = $"search-ms:displayname={Uri.EscapeDataString(q + " - " + a.Current.DisplayName)}&crumb=location:{Uri.EscapeDataString(loc)}&query={Uri.EscapeDataString(q)}";
        var p = Pidl.Parse(uri);
        if (p != null) a.Navigate(p);
    }

    private string _lastSearchLocation;

    private void ShowHistoryMenu(Rectangle screen)
    {
        var a = Active;
        if (a == null) return;
        var m = NewMenu();
        for (int i = a.History.Count - 1; i >= 0; i--)
        {
            int idx = i;
            var it = Item(m, a.History[i].DisplayName, ShellIcons.Small(a.History[i]), () => a.GoToHistory(idx));
            if (i == a.HistoryIndex) it.Font = new Font(m.Font, FontStyle.Bold);
        }
        ShowBelow(m, screen);
    }

    private void ShowAddressHistory(Rectangle screen)
    {
        var m = NewMenu();
        m.MaximumSize = new Size(screen.Width, S(600));
        foreach (var h in Store.History.Take(30))
        {
            var path = h;
            Item(m, path, ShellIcons.SmallForPath(path), () => NavigateAddress(path));
        }
        if (m.Items.Count == 0) Item(m, "（没有历史记录）", null, () => { }).Enabled = false;
        m.Width = screen.Width;
        ShowBelow(m, screen);
    }

    private void ShowSubfolders(Pidl parent, Rectangle screen)
    {
        var m = NewMenu();
        string fs = parent.FileSystemPath;
        if (fs != null)
        {
            var ss = new SHELLSTATE();
            Win32.SHGetSetSettings(ref ss, SSF_SHOWALLOBJECTS, false);
            bool showHidden = (ss.Flags1 & 1) != 0;
            var folderIcon = ShellIcons.SmallForPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            try
            {
                var dirs = new DirectoryInfo(fs).EnumerateDirectories()
                    .Where(d => showHidden || (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .Select(d => d.FullName).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).Take(300);
                foreach (var d in dirs)
                {
                    var path = d;
                    Item(m, Path.GetFileName(path), folderIcon, () => Active?.Navigate(path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        else if (parent.ParsingName.Equals("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                using var dp = Pidl.Parse(drive.Name);
                if (dp == null) continue;
                var path = drive.Name;
                Item(m, dp.DisplayName, ShellIcons.Small(dp), () => Active?.Navigate(path));
            }
        }
        if (m.Items.Count == 0) { m.Dispose(); return; }
        m.MaximumSize = new Size(S(500), S(800));
        ShowBelow(m, screen);
    }

    // ---------------- 收藏栏 ----------------

    private void WireFav()
    {
        // 和 360 一样：点收藏总是新开一个标签，不替换当前标签
        _fav.Open += (bm, _) => OpenBookmark(bm, true);
        _fav.ItemMenu += (bm, p) =>
        {
            var m = NewMenu();
            var open = Item(m, "在新标签页中打开", Ico("tab_add_fore"), () => OpenBookmark(bm, true));
            open.Font = new Font(m.Font, FontStyle.Bold);
            Item(m, "在当前标签页中打开", null, () => OpenBookmark(bm, false));
            m.Items.Add(new ToolStripSeparator());
            Item(m, "重命名", Ico("tool_rename"), () =>
            {
                var name = Prompt.Ask(this, "重命名收藏", bm.Name);
                if (!string.IsNullOrWhiteSpace(name)) { bm.Name = name.Trim(); Store.SaveBookmarks(); }
            });
            Item(m, "删除", Ico("tool_delete"), () => { Store.Bookmarks.Remove(bm); Store.SaveBookmarks(); });
            m.Items.Add(new ToolStripSeparator());
            Item(m, "收藏当前文件夹", Ico("favorite_icon"), AddCurrentToFavorites);
            m.Show(_fav, p);
        };
        _fav.BarMenu += p =>
        {
            var m = NewMenu();
            Item(m, "收藏当前文件夹", Ico("favorite_icon"), AddCurrentToFavorites);
            Item(m, "从 360 文件夹重新导入收藏", Ico("menu_sysexplorer"), ReimportFrom360);
            m.Show(_fav, p);
        };
        _fav.MoreMenu += (hidden, r) =>
        {
            var m = NewMenu();
            foreach (var bm in hidden) { var b = bm; Item(m, b.Name, ShellIcons.SmallForPath(b.Path), () => OpenBookmark(b, true)); }
            if (hidden.Count > 0) m.Items.Add(new ToolStripSeparator());
            Item(m, "收藏当前文件夹", Ico("favorite_icon"), AddCurrentToFavorites);
            Item(m, "从 360 文件夹重新导入收藏", Ico("menu_sysexplorer"), ReimportFrom360);
            ShowBelow(m, _fav.RectangleToScreen(r));
        };
    }

    private void OpenBookmark(Bookmark bm, bool newTab)
    {
        var p = Pidl.Parse(Environment.ExpandEnvironmentVariables(bm.Path));
        if (p == null)
        {
            MessageBox.Show(this, $"打不开“{bm.Path}”，可能已被移动或删除。", "ReFileBrowser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (File.Exists(bm.Path) && !Directory.Exists(bm.Path)) { p.Dispose(); Launch(bm.Path, null); return; }
        if (newTab) AddTab(p, true, _title.Selected + 1);
        else { Active?.Navigate(p); Active?.FocusView(); }
    }

    private void AddCurrentToFavorites()
    {
        var cur = Active?.Current;
        if (cur == null) return;
        string path = cur.FileSystemPath ?? cur.ParsingName;
        if (Store.Bookmarks.Any(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        Store.Bookmarks.Add(new Bookmark { Name = cur.DisplayName, Path = path });
        Store.SaveBookmarks();
    }

    private void ReimportFrom360()
    {
        var list = Store.Import360Bookmarks();
        int added = 0;
        foreach (var bm in list)
        {
            if (Store.Bookmarks.Any(b => string.Equals(b.Path, bm.Path, StringComparison.OrdinalIgnoreCase))) continue;
            Store.Bookmarks.Add(bm); added++;
        }
        Store.SaveBookmarks();
        MessageBox.Show(this, $"从 360 导入了 {added} 条新收藏（共读到 {list.Count} 条）。", "ReFileBrowser");
    }

    // ---------------- 标签菜单 / 主菜单 ----------------

    private void ShowTabMenu(int i, Point screen)
    {
        var t = Tabs[i];
        var m = NewMenu();
        Item(m, "新建标签页", Ico("tab_add_fore"), () => AddTab(Pidl.ThisPC(), true, i + 1));
        Item(m, "复制标签页", null, () => AddTab(t.Current?.Clone(), true, i + 1));
        Item(m, "在新窗口中打开", null, () => AppHost.Instance.OpenWindow(t.Current?.Clone()));
        m.Items.Add(new ToolStripSeparator());
        Item(m, "关闭标签页", Ico("tab_close_fore"), () => CloseTab(Tabs.IndexOf(t)));
        Item(m, "关闭其他标签页", null, () => { foreach (var o in Tabs.Where(x => x != t).ToList()) CloseTab(Tabs.IndexOf(o)); }).Enabled = Tabs.Count > 1;
        Item(m, "关闭右侧标签页", null, () => { while (Tabs.Count > Tabs.IndexOf(t) + 1) CloseTab(Tabs.Count - 1); }).Enabled = i < Tabs.Count - 1;
        Item(m, "重新打开关闭的标签页", null, ReopenClosedTab).Enabled = _closedTabs.Count > 0;
        m.Show(screen);
    }

    private void ShowTabList(Rectangle screen)
    {
        var m = NewMenu();
        for (int i = 0; i < Tabs.Count; i++)
        {
            int idx = i;
            var it = Item(m, Tabs[i].Title, Tabs[i].Icon, () => SelectTab(idx));
            if (i == _title.Selected) it.Font = new Font(m.Font, FontStyle.Bold);
        }
        m.Items.Add(new ToolStripSeparator());
        Item(m, "新建标签页", Ico("tab_add_fore"), () => AddTab(Pidl.ThisPC(), true));
        m.Show(new Point(screen.Right - m.PreferredSize.Width, screen.Bottom + S(8)));
    }

    private void ShowMainMenu(Rectangle screen)
    {
        var m = NewMenu();
        Item(m, "添加桌面图标", Ico("menu_shortcut"), () =>
        {
            Integration.CreateDesktopShortcut();
            MessageBox.Show(this, "已在桌面创建 ReFileBrowser 快捷方式。", "ReFileBrowser");
        });
        bool isDefault = Integration.IsDefault();
        Item(m, isDefault ? "恢复系统文件管理" : "设为默认文件管理（接管 Win+E）", Ico("menu_sysexplorer"), () =>
        {
            string q = isDefault
                ? "恢复后 Win+E 和双击文件夹将使用接管前的程序。需要管理员权限，继续吗？"
                : "设为默认后 Win+E 和双击文件夹会用 ReFileBrowser 打开，原设置会先备份。需要管理员权限，继续吗？";
            if (MessageBox.Show(this, q, "ReFileBrowser", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Integration.RunElevated(isDefault ? "--unregister" : "--register");
        });
        Item(m, "开机预加载（打开更快）", Ico("menu_update"), () => Integration.SetAutostart(!Integration.IsAutostart()), Integration.IsAutostart());
        m.Items.Add(new ToolStripSeparator());
        Item(m, "退出", Ico("menu_exit"), () => AppHost.Instance.Exit());
        m.Show(new Point(screen.Right - m.PreferredSize.Width, screen.Bottom));
    }
}
