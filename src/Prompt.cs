namespace ReFileBrowser;

/// <summary>最简单的单行输入框（重命名收藏用）。</summary>
public static class Prompt
{
    public static string Ask(IWin32Window owner, string title, string initial)
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96), ClientSize = new Size(360, 92),
        };
        var box = new TextBox { Text = initial, Left = 12, Top = 14, Width = 336 };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 192, Top = 52, Width = 75 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 273, Top = 52, Width = 75 };
        f.Controls.AddRange(new Control[] { box, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        f.Shown += (_, _) => { box.Focus(); box.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
