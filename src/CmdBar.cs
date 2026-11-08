namespace ReFileBrowser;

/// <summary>
/// 第三行工具栏，照抄 360 ToolContainer：高 48，inset 12，子间距 4。
/// 新建、视图带下拉箭头；复制/剪切/粘贴/重命名/删除用 360 的 tool_*.png 和 tool_*_disabled.png。
/// </summary>
public sealed class CmdBar : Control
{
    public enum Cmd { New, View, Copy, Cut, Paste, Rename, Delete }

    private sealed class Btn
    {
        public Cmd Cmd; public string Img; public string Tip; public bool Drop; public bool Enabled = true; public Rectangle Rect;
    }

    private readonly List<Btn> _btns = new()
    {
        new() { Cmd = Cmd.New, Img = "tool_new", Tip = "新建", Drop = true },
        new() { Cmd = Cmd.View, Img = "tool_view", Tip = "查看", Drop = true },
        new() { Cmd = Cmd.Copy, Img = "tool_copy", Tip = "复制 (Ctrl+C)" },
        new() { Cmd = Cmd.Cut, Img = "tool_cut", Tip = "剪切 (Ctrl+X)" },
        new() { Cmd = Cmd.Paste, Img = "tool_paste", Tip = "粘贴 (Ctrl+V)" },
        new() { Cmd = Cmd.Rename, Img = "tool_rename", Tip = "重命名 (F2)" },
        new() { Cmd = Cmd.Delete, Img = "tool_delete", Tip = "删除 (Delete)" },
    };

    private readonly ToolTip _tip = new();
    private int _hot = -1, _pressed = -1;

    public event Action<Cmd, Rectangle> Clicked;

    public CmdBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.White;
    }

    private int S(int v) => Theme.S(v, DeviceDpi);

    public void SetEnabled(Cmd cmd, bool enabled)
    {
        var b = _btns.First(x => x.Cmd == cmd);
        if (b.Enabled == enabled) return;
        b.Enabled = enabled;
        Invalidate(b.Rect);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int x = S(12);
        foreach (var b in _btns)
        {
            int w = b.Drop ? S(44) : S(36);
            b.Rect = new Rectangle(x, (Height - S(36)) / 2, w, S(36));
            x += w + S(4);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int dpi = DeviceDpi;
        g.Clear(Color.White);
        for (int i = 0; i < _btns.Count; i++)
        {
            var b = _btns[i];
            if (b.Enabled && i == _hot)
                Theme.DrawNine(g, Theme.Img(i == _pressed ? "tab_add_pushed" : "tab_add_hot", 96), b.Rect, 5, 5, 5, 5, dpi);
            var img = Theme.Img(b.Enabled ? b.Img : b.Img + "_disabled", dpi);
            if (b.Drop)
            {
                var iconR = new Rectangle(b.Rect.Left + S(8), b.Rect.Top, S(16), b.Rect.Height);
                Theme.DrawCentered(g, img, iconR, dpi);
                var arrowR = new Rectangle(iconR.Right + S(4), b.Rect.Top, S(8), b.Rect.Height);
                Theme.DrawCentered(g, Theme.Cell("tool_arrow", dpi, 4, b.Enabled ? 0 : 3), arrowR, dpi, 8);
            }
            else Theme.DrawCentered(g, img, b.Rect, dpi);
        }
    }

    private int Hit(Point p) => _btns.FindIndex(b => b.Rect.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = Hit(e.Location);
        if (h == _hot) return;
        _hot = h;
        Invalidate();
        if (h >= 0) _tip.Show(_btns[h].Tip, this, _btns[h].Rect.Left, _btns[h].Rect.Bottom + S(2), 2000);
        else _tip.Hide(this);
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; _tip.Hide(this); Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _pressed = Hit(e.Location);
        Invalidate();
        if (_pressed >= 0 && _btns[_pressed].Drop && _btns[_pressed].Enabled)
        {
            _tip.Hide(this);
            var b = _btns[_pressed];
            _pressed = -1;
            Clicked?.Invoke(b.Cmd, b.Rect);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int h = Hit(e.Location);
        int p = _pressed;
        _pressed = -1;
        Invalidate();
        if (e.Button == MouseButtons.Left && h >= 0 && h == p && _btns[h].Enabled && !_btns[h].Drop)
            Clicked?.Invoke(_btns[h].Cmd, _btns[h].Rect);
    }
}
