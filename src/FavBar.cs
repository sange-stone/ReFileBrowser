namespace ReFileBrowser;

/// <summary>
/// 第二行收藏栏，照抄 360：高 30，左边 inset 8，星标 16，书签项高 24 间距 4，右侧「更多」按钮。
/// 左键当前标签打开，中键/Ctrl+左键新标签打开；右键菜单；可拖动排序；把文件夹拖进来就添加。
/// </summary>
public sealed class FavBar : Control
{
    public event Action<Bookmark, bool> Open;          // bool = 新标签
    public event Action<Bookmark, Point> ItemMenu;
    public event Action<Point> BarMenu;
    public event Action<List<Bookmark>, Rectangle> MoreMenu;

    private readonly List<(Bookmark bm, Rectangle rect)> _items = new();
    private readonly List<Bookmark> _hidden = new();
    private Rectangle _star, _more;
    private int _hot = -1, _pressed = -1;
    private bool _moreHot;
    private int _dragFrom = -1; private Point _dragStart; private bool _dragging;

    public FavBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.White;
        AllowDrop = true;
        Store.BookmarksChanged += () => { if (!IsDisposed) { DoLayout(); Invalidate(); } };
    }

    private int S(int v) => Theme.S(v, DeviceDpi);

    protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); }

    public void DoLayout()
    {
        _items.Clear(); _hidden.Clear();
        int h = Height;
        _star = new Rectangle(S(8), (h - S(16)) / 2, S(16), S(16));
        _more = new Rectangle(Width - S(8) - S(16), (h - S(16)) / 2, S(16), S(16));
        int x = _star.Right + S(4);
        int limit = _more.Left - S(16);
        using var font = Theme.UiFont(DeviceDpi);
        int chrome = S(6) + S(16) + S(4) + S(6);   // 左边距 + 图标 + 间距 + 右边距
        int minW = chrome + S(36);                  // 至少要能显示两三个字加省略号
        bool full = false;
        foreach (var bm in Store.Bookmarks)
        {
            if (full) { _hidden.Add(bm); continue; }
            int tw = TextRenderer.MeasureText(bm.Name, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int w = chrome + Math.Min(tw, S(160));
            if (x + w > limit)
            {
                // 和 360 一样：放不下的那一条先截短显示，剩余空间连最小宽度都不够才收进「更多」
                full = true;
                w = limit - x;
                if (w < minW) { _hidden.Add(bm); continue; }
            }
            _items.Add((bm, new Rectangle(x, (h - S(24)) / 2, w, S(24))));
            x += w + S(4);
        }
    }

    private int HitItem(Point p)
    {
        for (int i = 0; i < _items.Count; i++) if (_items[i].rect.Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int dpi = DeviceDpi;
        g.Clear(Color.White);
        Theme.DrawCentered(g, Theme.Img("favorite_icon", 96), _star, dpi);
        using var font = Theme.UiFont(dpi);
        if (Store.Bookmarks.Count == 0)
        {
            var tip = Rectangle.FromLTRB(_star.Right + S(6), 0, _more.Left, Height);
            TextRenderer.DrawText(g, "可拖动文件夹到此，点击快速打开", font, tip, Theme.TextGray,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        for (int i = 0; i < _items.Count; i++)
        {
            var (bm, r) = _items[i];
            if (i == _hot)
                Theme.DrawNine(g, Theme.Img(i == _pressed ? "tab_add_pushed" : "tab_add_hot", 96), r, 5, 5, 5, 5, dpi);
            var icon = new Rectangle(r.Left + S(6), r.Top + (r.Height - S(16)) / 2, S(16), S(16));
            Theme.DrawCentered(g, ShellIcons.SmallForPath(bm.Path), icon, dpi);
            var tr = Rectangle.FromLTRB(icon.Right + S(4), r.Top, r.Right - S(6), r.Bottom);
            TextRenderer.DrawText(g, bm.Name, font, tr, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        if (_moreHot) Theme.DrawNine(g, Theme.Img("tab_add_hot", 96), _more, 5, 5, 5, 5, dpi);
        Theme.DrawCentered(g, Theme.Img("favorite_more", dpi), _more, dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom >= 0 && e.Button == MouseButtons.Left)
        {
            if (!_dragging && Math.Abs(e.X - _dragStart.X) > S(6)) _dragging = true;
            if (_dragging)
            {
                int to = HitItem(new Point(e.X, Height / 2));
                if (to >= 0 && to != _dragFrom)
                {
                    var list = Store.Bookmarks;
                    var bm = list[_dragFrom]; list.RemoveAt(_dragFrom); list.Insert(to, bm);
                    _dragFrom = to; _pressed = to; _hot = to;
                    DoLayout(); Invalidate();
                }
            }
            return;
        }
        int h = HitItem(e.Location);
        bool mh = _more.Contains(e.Location);
        if (h != _hot || mh != _moreHot) { _hot = h; _moreHot = mh; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; _moreHot = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_more.Contains(e.Location)) { MoreMenu?.Invoke(new List<Bookmark>(_hidden), _more); return; }
        _pressed = HitItem(e.Location);
        _dragFrom = _pressed; _dragStart = e.Location; _dragging = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int h = HitItem(e.Location);
        bool wasDrag = _dragging;
        int pressed = _pressed;
        _pressed = -1; _dragFrom = -1; _dragging = false;
        Invalidate();
        if (wasDrag) { Store.SaveBookmarks(); return; }
        if (e.Button == MouseButtons.Left && h >= 0 && h == pressed)
            Open?.Invoke(_items[h].bm, (ModifierKeys & Keys.Control) != 0);
        else if (e.Button == MouseButtons.Middle && h >= 0)
            Open?.Invoke(_items[h].bm, true);
        else if (e.Button == MouseButtons.Right)
        {
            if (h >= 0) ItemMenu?.Invoke(_items[h].bm, e.Location);
            else BarMenu?.Invoke(e.Location);
        }
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Link : DragDropEffects.None;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Link : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var p in paths)
        {
            if (Store.Bookmarks.Any(b => string.Equals(b.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            string name = Path.GetFileName(p.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = p;
            Store.Bookmarks.Add(new Bookmark { Name = name, Path = p });
        }
        Store.SaveBookmarks();
    }
}
