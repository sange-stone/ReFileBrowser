namespace ReFileBrowser;

/// <summary>
/// 第四行：后退/前进/历史/上级 + 面包屑地址栏 + 搜索框。尺寸照抄 360 explorer_frame.xml：
/// 高 40，inset 12,0,8,0，子间距 8；地址框高 32 边框 #D9D9D9；搜索框宽 200。
/// </summary>
public sealed class NavBar : Control
{
    private enum Part { None, Back, Forward, More, Up, Icon, Overflow, Segment, SegArrow, Empty, Drop, Refresh, SearchBtn }

    private sealed class Seg
    {
        public Pidl Pidl;
        public string Text;
        public Rectangle TextRect, ArrowRect;
        public bool Visible;
    }

    public event Action Back, Forward, Up, RefreshClicked;
    public event Action<Rectangle> HistoryMenu, DropMenu;
    public event Action<Pidl> NavigateTo;
    public event Action<Pidl, Rectangle> SubfolderMenu;
    public event Action<List<Pidl>, Rectangle> OverflowMenu;
    public event Action<string> AddressEntered, SearchEntered;

    private readonly TextBox _edit = new() { BorderStyle = BorderStyle.None, Visible = false };
    private readonly TextBox _search = new() { BorderStyle = BorderStyle.None, PlaceholderText = "快速搜索" };
    private readonly List<Seg> _segs = new();
    private Pidl _current;
    private Bitmap _icon;
    private bool _canBack, _canFwd, _canUp;

    private Rectangle _back, _fwd, _more, _up, _addr, _iconR, _crumb, _overflow, _drop, _refresh, _searchBox, _searchBtn;
    private (Part part, int seg) _hot = (Part.None, -1), _pressed = (Part.None, -1);

    public NavBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.White;
        Controls.Add(_edit);
        Controls.Add(_search);
        _edit.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; var t = _edit.Text; EndEdit(); AddressEntered?.Invoke(t); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndEdit(); EditCancelled?.Invoke(); }
        };
        _edit.LostFocus += (_, _) => EndEdit();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchEntered?.Invoke(_search.Text); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; _search.Text = ""; EditCancelled?.Invoke(); }
        };
    }

    public event Action EditCancelled;

    private int S(int v) => Theme.S(v, DeviceDpi);

    public bool Editing => _edit.Visible;

    public void SetState(Pidl current, bool canBack, bool canFwd, bool canUp)
    {
        _canBack = canBack; _canFwd = canFwd; _canUp = canUp;
        if (current != null && (_current == null || !_current.SameAs(current)))
        {
            _current = current.Clone();
            _icon = ShellIcons.Small(current);
            foreach (var s in _segs) s.Pidl.Dispose();
            _segs.Clear();
            var p = current.Clone();
            while (p != null && !p.IsDesktop)
            {
                _segs.Insert(0, new Seg { Pidl = p, Text = p.DisplayName });
                p = p.Parent();
            }
            if (_segs.Count == 0) _segs.Add(new Seg { Pidl = current.Clone(), Text = current.DisplayName });
            p?.Dispose();
            _search.PlaceholderText = $"在 {current.DisplayName} 中搜索";
            if (Editing) _edit.Text = current.EditingName;
        }
        DoLayout();
        Invalidate();
    }

    public void BeginEdit()
    {
        if (_current == null) return;
        _edit.Text = _current.FileSystemPath ?? _current.EditingName;
        _edit.Visible = true;
        DoLayout();
        _edit.Focus();
        _edit.SelectAll();
        Invalidate();
    }

    private void EndEdit()
    {
        if (!_edit.Visible) return;
        _edit.Visible = false;
        Invalidate();
    }

    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }
    public bool SearchFocused => _search.Focused;
    public void ClearSearch() => _search.Text = "";

    protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); }

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); DoLayout(); }

    private void DoLayout()
    {
        int h = Height;
        int cy(int sz) => (h - sz) / 2;
        int x = S(12);
        _back = new Rectangle(x, cy(S(16)), S(16), S(16)); x = _back.Right + S(8);
        _fwd = new Rectangle(x, cy(S(16)), S(16), S(16)); x = _fwd.Right + S(8);
        _more = new Rectangle(x, cy(S(16)), S(16), S(16)); x = _more.Right + S(8);
        _up = new Rectangle(x, cy(S(24)), S(24), S(24)); x = _up.Right + S(8);

        int right = Width - S(8);
        _searchBox = new Rectangle(right - S(200), cy(S(32)), S(200), S(32));
        _searchBtn = new Rectangle(_searchBox.Right - S(8) - S(16), cy(S(16)), S(16), S(16));
        _addr = Rectangle.FromLTRB(x, cy(S(32)), _searchBox.Left - S(8), cy(S(32)) + S(32));

        int ax = _addr.Left + S(8);
        _iconR = new Rectangle(ax, cy(S(16)), S(16), S(16)); ax = _iconR.Right + S(2);
        _refresh = new Rectangle(_addr.Right - S(4) - S(24), cy(S(24)), S(24), S(24));
        _drop = new Rectangle(_refresh.Left - S(24), cy(S(24)), S(24), S(24));
        _crumb = Rectangle.FromLTRB(ax, _addr.Top + S(1), _drop.Left, _addr.Bottom - S(1));

        int editH = _edit.PreferredHeight;
        _edit.Bounds = new Rectangle(_crumb.Left + S(2), _addr.Top + (_addr.Height - editH) / 2, Math.Max(10, _crumb.Width - S(4)), editH);
        int sh = _search.PreferredHeight;
        _search.Bounds = new Rectangle(_searchBox.Left + S(6), _searchBox.Top + (_searchBox.Height - sh) / 2,
            Math.Max(10, _searchBtn.Left - _searchBox.Left - S(10)), sh);

        LayoutSegments();
    }

    private void LayoutSegments()
    {
        using var font = Theme.UiFont(DeviceDpi);
        int arrowW = S(16), pad = S(4);
        var widths = _segs.Select(s => TextRenderer.MeasureText(s.Text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + pad * 2).ToList();
        int avail = _crumb.Width - S(4);
        int used = 0, first = _segs.Count;
        for (int i = _segs.Count - 1; i >= 0; i--)
        {
            int w = widths[i] + arrowW;
            int extra = i > 0 ? S(12) : 0; // 如果前面还有被藏的，要给 « 留位
            if (used + w + extra > avail && i < _segs.Count - 1) break;
            used += w; first = i;
        }
        int x = _crumb.Left;
        _overflow = Rectangle.Empty;
        if (first > 0) { _overflow = new Rectangle(x, _crumb.Top + (_crumb.Height - S(24)) / 2, S(12), S(24)); x = _overflow.Right; }
        for (int i = 0; i < _segs.Count; i++)
        {
            var s = _segs[i];
            s.Visible = i >= first;
            if (!s.Visible) continue;
            int ty = _crumb.Top + (_crumb.Height - S(24)) / 2;
            s.TextRect = new Rectangle(x, ty, Math.Min(widths[i], Math.Max(S(20), _crumb.Right - x - arrowW)), S(24));
            x = s.TextRect.Right;
            s.ArrowRect = new Rectangle(x, ty, arrowW, S(24));
            x = s.ArrowRect.Right;
        }
    }

    private (Part part, int seg) HitTest(Point p)
    {
        if (_back.Contains(p)) return (Part.Back, -1);
        if (_fwd.Contains(p)) return (Part.Forward, -1);
        if (_more.Contains(p)) return (Part.More, -1);
        if (_up.Contains(p)) return (Part.Up, -1);
        if (_refresh.Contains(p)) return (Part.Refresh, -1);
        if (_drop.Contains(p)) return (Part.Drop, -1);
        if (_searchBtn.Contains(p)) return (Part.SearchBtn, -1);
        if (!Editing)
        {
            if (!_overflow.IsEmpty && _overflow.Contains(p)) return (Part.Overflow, -1);
            for (int i = 0; i < _segs.Count; i++)
            {
                if (!_segs[i].Visible) continue;
                if (_segs[i].TextRect.Contains(p)) return (Part.Segment, i);
                if (_segs[i].ArrowRect.Contains(p)) return (Part.SegArrow, i);
            }
            if (_crumb.Contains(p) || _iconR.Contains(p)) return (Part.Empty, -1);
        }
        return (Part.None, -1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int dpi = DeviceDpi;
        g.Clear(Color.White);

        DrawStrip(g, "nav_back", _back, Part.Back, _canBack, dpi);
        DrawStrip(g, "nav_forward", _fwd, Part.Forward, _canFwd, dpi);
        DrawStrip(g, "nav_more", _more, Part.More, _canBack || _canFwd, dpi);
        if (_canUp && _hot.part == Part.Up)
            Theme.DrawNine(g, Theme.Img(_pressed.part == Part.Up ? "tab_add_pushed" : "tab_add_hot", 96), _up, 10, 10, 10, 10, 96);
        DrawStrip(g, "nav_up", new Rectangle(_up.X + (_up.Width - S(16)) / 2, _up.Y + (_up.Height - S(16)) / 2, S(16), S(16)), Part.Up, _canUp, dpi);

        Theme.DrawBorder(g, _addr, Theme.EditBorder, dpi);
        Theme.DrawBorder(g, _searchBox, Theme.EditBorder, dpi);
        if (_icon != null) Theme.DrawCentered(g, _icon, _iconR, dpi);

        if (!Editing)
        {
            using var font = Theme.UiFont(dpi);
            if (!_overflow.IsEmpty)
            {
                HotBg(g, _overflow, Part.Overflow, -1);
                DrawArrow(g, _overflow, dpi, true);
            }
            for (int i = 0; i < _segs.Count; i++)
            {
                var s = _segs[i];
                if (!s.Visible) continue;
                bool hotSeg = _hot.seg == i && (_hot.part == Part.Segment || _hot.part == Part.SegArrow);
                if (hotSeg)
                {
                    var whole = Rectangle.Union(s.TextRect, s.ArrowRect);
                    using var br = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0));
                    g.FillRectangle(br, whole);
                    using var pen = new Pen(Color.FromArgb(0xE0, 0xE0, 0xE0));
                    g.DrawLine(pen, s.ArrowRect.Left, s.ArrowRect.Top + S(4), s.ArrowRect.Left, s.ArrowRect.Bottom - S(4));
                    if (_hot.part != Part.None && _pressed.seg == i)
                    {
                        using var pb = new SolidBrush(Theme.Pushed);
                        g.FillRectangle(pb, _pressed.part == Part.SegArrow ? s.ArrowRect : s.TextRect);
                    }
                }
                TextRenderer.DrawText(g, s.Text, font, s.TextRect, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                DrawArrow(g, s.ArrowRect, dpi, false);
            }
        }

        HotBg(g, _drop, Part.Drop, -1);
        Theme.DrawCentered(g, Theme.Cell("tool_arrow", dpi, 4, 0), _drop, dpi, 8);
        HotBg(g, _refresh, Part.Refresh, -1);
        Theme.DrawCentered(g, Theme.Img("addr_refresh", dpi), _refresh, dpi);
        Theme.DrawCentered(g, Theme.Img("edit_search", dpi), _searchBtn, dpi);
    }

    private void DrawArrow(Graphics g, Rectangle r, int dpi, bool left)
    {
        var img = Theme.Img("addr_btn_arrow", dpi);
        if (!left) { Theme.DrawCentered(g, img, r, dpi, 8); return; }
        using var flipped = (Bitmap)img.Clone();
        flipped.RotateFlip(RotateFlipType.RotateNoneFlipX);
        Theme.DrawCentered(g, flipped, r, dpi, 8);
    }

    private void HotBg(Graphics g, Rectangle r, Part part, int seg)
    {
        if (_hot.part != part || _hot.seg != seg) return;
        Theme.DrawNine(g, Theme.Img(_pressed.part == part ? "tab_add_pushed" : "tab_add_hot", 96), r, 5, 10, 5, 10, 96);
    }

    private void DrawStrip(Graphics g, string name, Rectangle r, Part part, bool enabled, int dpi)
    {
        int idx = !enabled ? 3 : _pressed.part == part && _hot.part == part ? 2 : _hot.part == part ? 1 : 0;
        Theme.DrawCentered(g, Theme.Cell(name, dpi, 4, idx), r, dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitTest(e.Location);
        if (h != _hot) { _hot = h; Invalidate(); }
        Cursor = h.part == Part.Empty ? Cursors.IBeam : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = (Part.None, -1);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _pressed = HitTest(e.Location);
        var r = RectFor(_pressed);
        // 下拉类按钮在按下时就弹出，和系统资源管理器一致
        switch (_pressed.part)
        {
            case Part.More: if (_canBack || _canFwd) HistoryMenu?.Invoke(r); _pressed = (Part.None, -1); break;
            case Part.Drop: DropMenu?.Invoke(_addr); _pressed = (Part.None, -1); break;
            case Part.SegArrow: SubfolderMenu?.Invoke(_segs[_pressed.seg].Pidl, r); _pressed = (Part.None, -1); break;
            case Part.Overflow: OverflowMenu?.Invoke(_segs.Where(s => !s.Visible).Select(s => s.Pidl).ToList(), r); _pressed = (Part.None, -1); break;
            case Part.Empty: _pressed = (Part.None, -1); BeginEdit(); break;
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var h = HitTest(e.Location);
        var p = _pressed;
        _pressed = (Part.None, -1);
        Invalidate();
        if (e.Button == MouseButtons.Middle && h.part == Part.Segment) { MiddleNavigate?.Invoke(_segs[h.seg].Pidl); return; }
        if (e.Button != MouseButtons.Left || p != h) return;
        switch (h.part)
        {
            case Part.Back: if (_canBack) Back?.Invoke(); break;
            case Part.Forward: if (_canFwd) Forward?.Invoke(); break;
            case Part.Up: if (_canUp) Up?.Invoke(); break;
            case Part.Refresh: RefreshClicked?.Invoke(); break;
            case Part.SearchBtn: SearchEntered?.Invoke(_search.Text); break;
            case Part.Segment: NavigateTo?.Invoke(_segs[h.seg].Pidl); break;
        }
    }

    public event Action<Pidl> MiddleNavigate;

    private Rectangle RectFor((Part part, int seg) h) => h.part switch
    {
        Part.More => _more,
        Part.Drop => _drop,
        Part.Overflow => _overflow,
        Part.SegArrow => _segs[h.seg].ArrowRect,
        _ => Rectangle.Empty,
    };
}
