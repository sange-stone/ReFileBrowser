using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ReFileBrowser;

/// <summary>
/// 配色、尺寸和界面图标。图标在 assets\icons，由 tools\make_icons.ps1 从 Microsoft Fluent UI System Icons（MIT）生成；
/// 布局尺寸和配色参考 360 文件管理器的界面。
/// </summary>
public static class Theme
{
    // 界面配色
    public static readonly Color Frame = Color.FromArgb(0xDF, 0xE8, 0xF1);
    public static readonly Color LineLight = Color.FromArgb(0xEF, 0xF4, 0xF8);
    public static readonly Color LineDark = Color.FromArgb(0xDC, 0xE1, 0xE5);
    public static readonly Color EditBorder = Color.FromArgb(0xD9, 0xD9, 0xD9);
    public static readonly Color Hot = Color.FromArgb(0x0C, 0, 0, 0);
    public static readonly Color Pushed = Color.FromArgb(0x19, 0, 0, 0);
    public static readonly Color CloseHot = Color.FromArgb(0xE8, 0x11, 0x23);
    public static readonly Color ClosePushed = Color.FromArgb(0xD0, 0x0F, 0x1F);
    public static readonly Color Separator = Color.FromArgb(0x99, 0x33, 0x33, 0x33);
    public static readonly Color Text = Color.FromArgb(0x1A, 0x1A, 0x1A);
    public static readonly Color TextGray = Color.FromArgb(0x99, 0x99, 0x99);
    public static readonly Color MenuLine = Color.FromArgb(0xE2, 0xE2, 0xE2);

    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "assets", "icons");
    private static readonly Dictionary<string, Bitmap> Cache = new();

    public static int S(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0);

    /// <summary>取图片；缩放 ≥150% 时优先用 name200.png。</summary>
    public static Bitmap Img(string name, int dpi)
    {
        bool hi = dpi >= 144 && File.Exists(Path.Combine(Dir, name + "200.png"));
        string key = hi ? name + "200" : name;
        if (Cache.TryGetValue(key, out var b)) return b;
        string path = Path.Combine(Dir, key + ".png");
        b = File.Exists(path) ? LoadPng(path) : new Bitmap(1, 1);
        Cache[key] = b;
        return b;
    }

    /// <summary>横条图（如 nav_back.png：正常/悬停/按下/禁用 4 格）取第 index 格。</summary>
    public static Bitmap Cell(string name, int dpi, int count, int index)
    {
        string key = $"{name}#{dpi >= 144}#{count}#{index}";
        if (Cache.TryGetValue(key, out var b)) return b;
        var strip = Img(name, dpi);
        int w = strip.Width / count;
        b = strip.Clone(new Rectangle(w * index, 0, w, strip.Height), PixelFormat.Format32bppArgb);
        Cache[key] = b;
        return b;
    }

    private static Bitmap LoadPng(string path)
    {
        using var fs = File.OpenRead(path);
        using var img = Image.FromStream(fs);
        return new Bitmap(img);
    }

    /// <summary>把 16 逻辑像素的图标画在 rect 中央（按 dpi 缩放）。</summary>
    public static void DrawCentered(Graphics g, Bitmap img, Rectangle rect, int dpi, int logicalSize = 16)
    {
        if (img == null) return;
        int sz = S(logicalSize, dpi);
        int w = img.Width == img.Height ? sz : (int)Math.Round(img.Width * sz / (double)img.Height);
        var dst = new Rectangle(rect.X + (rect.Width - w) / 2, rect.Y + (rect.Height - sz) / 2, w, sz);
        if (img.Width == dst.Width && img.Height == dst.Height)
            g.DrawImageUnscaled(img, dst.Location);
        else
        {
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, dst);
            g.InterpolationMode = old;
        }
    }

    /// <summary>按 360 的 corner 规则做九宫格拉伸（corner 为源图像素，目标按 dpi 放大）。</summary>
    public static void DrawNine(Graphics g, Bitmap img, Rectangle dst, int l, int t, int r, int b, int dpi)
    {
        if (img == null || dst.Width <= 0 || dst.Height <= 0) return;
        double k = dpi / 96.0;
        int dl = (int)(l * k), dt = (int)(t * k), dr = (int)(r * k), db = (int)(b * k);
        dl = Math.Min(dl, dst.Width / 2); dr = Math.Min(dr, dst.Width / 2);
        dt = Math.Min(dt, dst.Height / 2); db = Math.Min(db, dst.Height / 2);
        int sw = img.Width, sh = img.Height;
        int[] sx = { 0, l, sw - r, sw }, sy = { 0, t, sh - b, sh };
        int[] dx = { dst.Left, dst.Left + dl, dst.Right - dr, dst.Right }, dy = { dst.Top, dst.Top + dt, dst.Bottom - db, dst.Bottom };
        var oldI = g.InterpolationMode; var oldP = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var d = Rectangle.FromLTRB(dx[i], dy[j], dx[i + 1], dy[j + 1]);
                int w = sx[i + 1] - sx[i], h = sy[j + 1] - sy[j];
                if (d.Width <= 0 || d.Height <= 0 || w <= 0 || h <= 0) continue;
                g.DrawImage(img, d, sx[i], sy[j], w, h, GraphicsUnit.Pixel, ia);
            }
        g.InterpolationMode = oldI; g.PixelOffsetMode = oldP;
    }

    /// <summary>画 360 风格的边框：bordersize=1 是逻辑像素，200% 下要画 2px。</summary>
    public static void DrawBorder(Graphics g, Rectangle r, Color c, int dpi)
    {
        int t = Math.Max(1, S(1, dpi));
        using var br = new SolidBrush(c);
        g.FillRectangle(br, r.Left, r.Top, r.Width, t);
        g.FillRectangle(br, r.Left, r.Bottom - t, r.Width, t);
        g.FillRectangle(br, r.Left, r.Top, t, r.Height);
        g.FillRectangle(br, r.Right - t, r.Top, t, r.Height);
    }

    public static Font UiFont(int dpi, bool bold = false)
    {
        // 360 的 font size=12 是像素，换算成 9pt；这里按像素建字体，避免再次受 DPI 影响
        return new Font("Microsoft YaHei UI", S(12, dpi), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
    }
}

/// <summary>白底、无渐变的菜单/工具栏渲染，接近 360 的弹出菜单。</summary>
public sealed class FlatRenderer : ToolStripProfessionalRenderer
{
    public FlatRenderer() : base(new FlatColors()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var pen = new Pen(Color.FromArgb(0xCC, 0xCC, 0xCC));
            var r = e.AffectedBounds; r.Width--; r.Height--;
            e.Graphics.DrawRectangle(pen, r);
        }
    }

    private sealed class FlatColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuItemSelected => Color.FromArgb(0xEE, 0xF1, 0xF5);
        public override Color MenuItemBorder => Color.FromArgb(0xEE, 0xF1, 0xF5);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0xEE, 0xF1, 0xF5);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0xEE, 0xF1, 0xF5);
        public override Color MenuBorder => Color.FromArgb(0xCC, 0xCC, 0xCC);
        public override Color SeparatorDark => Theme.MenuLine;
        public override Color SeparatorLight => Color.White;
        public override Color ToolStripBorder => Color.White;
        public override Color ToolStripGradientBegin => Color.White;
        public override Color ToolStripGradientMiddle => Color.White;
        public override Color ToolStripGradientEnd => Color.White;
        public override Color ButtonSelectedHighlight => Theme.Hot;
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(0xF0, 0xF0, 0xF0);
        public override Color ButtonSelectedGradientMiddle => Color.FromArgb(0xF0, 0xF0, 0xF0);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(0xF0, 0xF0, 0xF0);
        public override Color ButtonSelectedBorder => Color.FromArgb(0xF0, 0xF0, 0xF0);
        public override Color ButtonPressedGradientBegin => Color.FromArgb(0xE4, 0xE4, 0xE4);
        public override Color ButtonPressedGradientMiddle => Color.FromArgb(0xE4, 0xE4, 0xE4);
        public override Color ButtonPressedGradientEnd => Color.FromArgb(0xE4, 0xE4, 0xE4);
        public override Color ButtonPressedBorder => Color.FromArgb(0xE4, 0xE4, 0xE4);
        public override Color CheckBackground => Color.FromArgb(0xEE, 0xF1, 0xF5);
        public override Color CheckSelectedBackground => Color.FromArgb(0xEE, 0xF1, 0xF5);
        public override Color CheckPressedBackground => Color.FromArgb(0xEE, 0xF1, 0xF5);
        public override Color OverflowButtonGradientBegin => Color.White;
        public override Color OverflowButtonGradientMiddle => Color.White;
        public override Color OverflowButtonGradientEnd => Color.White;
    }
}
