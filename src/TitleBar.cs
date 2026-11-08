namespace ReFileBrowser;

/// <summary>
/// 第一行：标签页 + 新建 + 标签计数 + 菜单/最小化/最大化/关闭。尺寸来自 360 的 explorer_frame.xml。
/// 空白处和最大化按钮在 WM_NCHITTEST 里返回 HTTRANSPARENT，由窗体给系统报 HTCAPTION / HTMAXBUTTON，
/// 这样拖动、双击最大化、Win11 贴靠布局都走系统自己的逻辑。
/// </summary>
public sealed class TitleBar : Control
{
    public enum Part { None, Tab, TabClose, Add, Count, Menu, Min, Max, Close }

    public List<BrowserHost> Tabs = new();
    public int Selected;
    public bool Maximized;

    public event Action<int> TabClicked;
    public event Action<int> TabCloseClicked;
    public event Action<int, int> TabMoved;
    public event Action<int, Point> TabRightClicked;
    public event Action<Part, Rectangle> ButtonClicked;
    /// <summary>标签被拖出标签栏：(索引, 鼠标屏幕坐标, 按下点相对标签左上角的偏移)</summary>
    public event Action<int, Point, Point> TabDetach;

    public Rectangle TabRect(int i) => i >= 0 && i < _tabRects.Length ? _tabRects[i] : new Rectangle(S(3), S(2), S(200), S(30));

    /// <summary>在标签栏 x 处松手时应插入的位置（0..Tabs.Count）。</summary>
    public int InsertIndexAt(Point p)
    {
        for (int i = 0; i < _tabRects.Length; i++)
            if (p.X < _tabRects[i].Left + _tabRects[i].Width / 2) return i;
        return _tabRects.Length;
    }

    /// <summary>别的窗口的标签刚并进来且鼠标仍按着：接着拖这个标签（可横向排序，也可再拖出去）。</summary>
    public void BeginExternalDrag(int index, Point grab)
    {
        var p = PointToClient(Cursor.Position);
        _dragIndex = index;
        _dragStartX = p.X; _dragStartY = p.Y;
        _dragGrab = grab;
        _dragging = true;
        _pressed = (Part.Tab, index);
        Capture = true;
        Invalidate();
    }

    private Rectangle[] _tabRects = Array.Empty<Rectangle>();
    private Rectangle _add, _count, _menu, _min, _max, _close;
    private int _sepX, _tabW, _tabStep, _tabLeft;
    private (Part part, int index) _hot = (Part.None, -1), _pressed = (Part.None, -1);
    private bool _ncMaxHot, _ncMaxPressed;
    private int _dragIndex = -1, _dragStartX, _dragStartY;
    private Point _dragGrab;
    private bool _dragging;

    public TitleBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.Frame;
    }

    private int S(int v) => Theme.S(v, DeviceDpi);

    public void RefreshTabs() { DoLayout(); Invalidate(); }

    protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); }

    private void DoLayout()
    {
        int x = Width;
        int b = S(32);
        _close = new Rectangle(x - b, 0, b, b); x -= b;
        _max = new Rectangle(x - b, 0, b, b); x -= b;
        _min = new Rectangle(x - b, 0, b, b); x -= b;
        _menu = new Rectangle(x - b, 0, b, b); x -= b;
        x -= S(4);
        _sepX = x - S(1); x -= S(1);
        x -= S(8);
        _count = new Rectangle(x - S(25), S(7), S(24), S(16)); x -= S(25);
        x -= S(30);
        int tabsRight = x - S(17);

        _tabLeft = S(3);
        int n = Math.Max(1, Tabs.Count);
        int overlap = S(8);
        int avail = Math.Max(0, tabsRight - _tabLeft);
        _tabW = Math.Clamp((avail + overlap * (n - 1)) / n, S(48), S(200));
        _tabStep = _tabW - overlap;
        _tabRects = new Rectangle[Tabs.Count];
        for (int i = 0; i < Tabs.Count; i++)
            _tabRects[i] = new Rectangle(_tabLeft + i * _tabStep, S(2), _tabW, S(30));
        int lastRight = Tabs.Count > 0 ? _tabRects[^1].Right : _tabLeft;
        _add = new Rectangle(lastRight + S(1), S(2) + (S(30) - S(16)) / 2, S(16), S(16));
    }

    private Rectangle CloseRectOf(Rectangle tab)
        => new(tab.Right - S(14) - S(16), tab.Top + (tab.Height - S(16)) / 2, S(16), S(16));

    public (Part part, int index) HitTest(Point p)
    {
        if (_close.Contains(p)) return (Part.Close, -1);
        if (_max.Contains(p)) return (Part.Max, -1);
        if (_min.Contains(p)) return (Part.Min, -1);
        if (_menu.Contains(p)) return (Part.Menu, -1);
        if (_count.Contains(p)) return (Part.Count, -1);
        if (_add.Contains(p)) return (Part.Add, -1);
        // 选中的标签画在最上层，先测
        int[] order = Enumerable.Range(0, _tabRects.Length).OrderBy(i => i == Selected ? 0 : 1).ToArray();
        foreach (int i in order)
        {
            var r = _tabRects[i];
            var inner = Rectangle.FromLTRB(r.Left + S(6), r.Top, r.Right - S(6), r.Bottom);
            if (!inner.Contains(p)) continue;
            if ((i == Selected || _hot.index == i) && CloseRectOf(r).Contains(p)) return (Part.TabClose, i);
            return (Part.Tab, i);
        }
        return (Part.None, -1);
    }

    /// <summary>给窗体用：这个点该报给系统什么非客户区代码。</summary>
    public int NcHit(Point p)
    {
        var (part, _) = HitTest(p);
        return part switch
        {
            Part.None => Win32.HTCAPTION,
            Part.Max => Win32.HTMAXBUTTON,
            _ => Win32.HTCLIENT,
        };
    }

    public void SetNcMax(bool hot, bool pressed)
    {
        if (hot == _ncMaxHot && pressed == _ncMaxPressed) return;
        _ncMaxHot = hot; _ncMaxPressed = pressed;
        Invalidate(_max);
    }

    public int TopResizeBand { get; set; }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_NCHITTEST)
        {
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            if (p.Y < TopResizeBand || NcHit(p) != Win32.HTCLIENT)
            {
                m.Result = (IntPtr)(-1); // HTTRANSPARENT：交给父窗口（主窗体）处理
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int dpi = DeviceDpi;
        g.Clear(Theme.Frame);
        using var font = Theme.UiFont(dpi);

        for (int i = 0; i < _tabRects.Length; i++)
            if (i != Selected) DrawTab(g, i, font, dpi);
        if (Selected >= 0 && Selected < _tabRects.Length) DrawTab(g, Selected, font, dpi);

        // 新建标签
        DrawBg(g, _add, Part.Add, "tab_add_hot", "tab_add_pushed", dpi);
        Theme.DrawCentered(g, Theme.Img("tab_add_fore", dpi), _add, dpi);

        // 标签计数框
        Theme.DrawBorder(g, _count, _hot.part == Part.Count ? Color.FromArgb(0x33, 0x33, 0x33) : Color.FromArgb(0x66, 0x66, 0x66), dpi);
        TextRenderer.DrawText(g, Tabs.Count.ToString(), font, _count, Color.FromArgb(0x33, 0x33, 0x33),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        using (var sep = new SolidBrush(Theme.Separator))
            g.FillRectangle(sep, _sepX, S(6), Math.Max(1, S(1) / 2), S(20));

        DrawCaption(g, _menu, Part.Menu, "menu_fore", dpi);
        DrawCaption(g, _min, Part.Min, "min_fore", dpi);
        DrawCaption(g, _max, Part.Max, Maximized ? "restore_fore" : "max_fore", dpi);
        DrawCaption(g, _close, Part.Close, "close_fore", dpi);
    }

    private void DrawTab(Graphics g, int i, Font font, int dpi)
    {
        var r = _tabRects[i];
        bool sel = i == Selected;
        bool hot = _hot.index == i && (_hot.part == Part.Tab || _hot.part == Part.TabClose);
        string img = sel ? "tab_pushed" : hot ? "tab_hot" : "tab_normal";
        Theme.DrawNine(g, Theme.Img(img, 96), r, 12, 8, 12, 4, dpi);

        var tab = Tabs[i];
        int x = r.Left + S(14);
        var iconRect = new Rectangle(x, r.Top + (r.Height - S(16)) / 2, S(16), S(16));
        if (tab.Icon != null) Theme.DrawCentered(g, tab.Icon, iconRect, dpi);
        x = iconRect.Right + S(6);

        bool showClose = sel || hot;
        var close = CloseRectOf(r);
        int textRight = (showClose ? close.Left : r.Right - S(14)) - S(2);
        var textRect = Rectangle.FromLTRB(x, r.Top, Math.Max(x, textRight), r.Bottom);
        TextRenderer.DrawText(g, tab.Title, font, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        if (showClose)
        {
            bool closeHot = _hot.part == Part.TabClose && _hot.index == i;
            bool closePushed = closeHot && _pressed.part == Part.TabClose && _pressed.index == i;
            if (closePushed) Theme.DrawCentered(g, Theme.Img("tab_close_pushed", dpi), close, dpi);
            else if (closeHot) Theme.DrawCentered(g, Theme.Img("tab_close_hot", dpi), close, dpi);
            Theme.DrawCentered(g, Theme.Img("tab_close_fore", dpi), close, dpi);
        }
    }

    private void DrawBg(Graphics g, Rectangle r, Part part, string hotImg, string pushedImg, int dpi)
    {
        if (_hot.part != part) return;
        var img = Theme.Img(_pressed.part == part ? pushedImg : hotImg, 96);
        Theme.DrawNine(g, img, r, 5, 5, 5, 5, dpi);
    }

    private void DrawCaption(Graphics g, Rectangle r, Part part, string fore, int dpi)
    {
        bool hot = _hot.part == part || (part == Part.Max && _ncMaxHot);
        bool pressed = (_pressed.part == part && hot) || (part == Part.Max && _ncMaxPressed);
        if (hot)
        {
            Color c = part == Part.Close ? (pressed ? Theme.ClosePushed : Theme.CloseHot) : (pressed ? Theme.Pushed : Theme.Hot);
            using var br = new SolidBrush(c);
            g.FillRectangle(br, r);
        }
        string img = part == Part.Close && hot ? "close_fore_white" : fore;
        Theme.DrawCentered(g, Theme.Img(img, dpi), r, dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragIndex >= 0 && e.Button == MouseButtons.Left)
        {
            if (!_dragging && (Math.Abs(e.X - _dragStartX) > S(4) || Math.Abs(e.Y - _dragStartY) > S(4))) _dragging = true;
            // 竖直方向拖出标签栏一定距离就把标签拉出去（和浏览器一样）
            // 只有一个标签时一拖就是拖窗口（可以拖到别的窗口里合并）
            if (_dragging && (Tabs.Count == 1 || e.Y < -S(20) || e.Y > Height + S(20)))
            {
                int i = _dragIndex;
                var grab = new Point(_dragGrab.X, _dragGrab.Y);
                _dragIndex = -1; _dragging = false; _pressed = (Part.None, -1);
                Capture = false;
                Invalidate();
                TabDetach?.Invoke(i, PointToScreen(e.Location), grab);
                return;
            }
            if (_dragging && _tabStep > 0)
            {
                int to = Math.Clamp((e.X - _tabLeft) / _tabStep, 0, Tabs.Count - 1);
                if (to != _dragIndex) { TabMoved?.Invoke(_dragIndex, to); _dragIndex = to; }
            }
            return;
        }
        var h = HitTest(e.Location);
        if (h != _hot) { _hot = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hot.part != Part.None) { _hot = (Part.None, -1); Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var h = HitTest(e.Location);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = h;
            if (h.part == Part.Tab)
            {
                TabClicked?.Invoke(h.index);
                _dragIndex = h.index; _dragStartX = e.X; _dragStartY = e.Y; _dragging = false;
                _dragGrab = new Point(e.X - _tabRects[h.index].Left, e.Y - _tabRects[h.index].Top);
            }
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var h = HitTest(e.Location);
        if (e.Button == MouseButtons.Left)
        {
            var p = _pressed;
            _pressed = (Part.None, -1);
            bool wasDrag = _dragging;
            _dragIndex = -1; _dragging = false;
            if (!wasDrag && p.part == h.part && p.index == h.index)
            {
                if (h.part == Part.TabClose) TabCloseClicked?.Invoke(h.index);
                else if (h.part is Part.Add or Part.Count or Part.Menu or Part.Min or Part.Max or Part.Close)
                    ButtonClicked?.Invoke(h.part, RectOf(h.part));
            }
            Invalidate();
        }
        else if (e.Button == MouseButtons.Middle && h.part is Part.Tab or Part.TabClose)
            TabCloseClicked?.Invoke(h.index);
        else if (e.Button == MouseButtons.Right && h.part is Part.Tab or Part.TabClose)
            TabRightClicked?.Invoke(h.index, e.Location);
    }

    public Rectangle RectOf(Part part) => part switch
    {
        Part.Add => _add,
        Part.Count => _count,
        Part.Menu => _menu,
        Part.Min => _min,
        Part.Max => _max,
        Part.Close => _close,
        _ => Rectangle.Empty,
    };
}
