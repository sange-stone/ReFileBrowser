using System.Diagnostics;
using System.Text;

namespace ReFileBrowser;

/// <summary>调试日志：只有设置了环境变量 REFILEBROWSER_LOG=1 才写 %TEMP%\refilebrowser.log。</summary>
public static class Log
{
    private static readonly bool On = Environment.GetEnvironmentVariable("REFILEBROWSER_LOG") == "1";
    public static bool Enabled => On;
    private static readonly string File = Path.Combine(Path.GetTempPath(), "refilebrowser.log");
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void W(string msg)
    {
        if (!On) return;
        try { System.IO.File.AppendAllText(File, $"{Clock.ElapsedMilliseconds,7} {msg}\r\n", Encoding.UTF8); }
        catch (IOException) { }
    }
}
