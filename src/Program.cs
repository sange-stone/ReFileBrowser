using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ReFileBrowser;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--register") return Integration.Register();
        if (args.Length == 1 && args[0] == "--unregister") return Integration.Unregister();

        // 已有常驻进程时：不加载 WinForms，直接把命令行转发过去就退出
        using var mutex = new Mutex(true, @"Local\ReFileBrowser.SingleInstance", out bool owner);
        if (!owner) return Forward(args) ? 0 : 1;

        RunApp(args);
        GC.KeepAlive(mutex);
        return 0;
    }

    private static bool Forward(string[] args)
    {
        string payload = Environment.CurrentDirectory + "\0" + string.Join("\0", args) + "\0";
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            IntPtr hwnd = Win32.FindWindowEx((IntPtr)(-3), IntPtr.Zero, null, AppHost.MsgWindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                Win32.GetWindowThreadProcessId(hwnd, out uint pid);
                Win32.AllowSetForegroundWindow(pid); // 让常驻进程能把窗口切到前台
                IntPtr buf = Marshal.StringToHGlobalUni(payload);
                try
                {
                    var cds = new COPYDATASTRUCT { dwData = (IntPtr)0x5346, cbData = payload.Length * 2, lpData = buf };
                    if (Win32.SendMessageTimeout(hwnd, Win32.WM_COPYDATA, IntPtr.Zero, ref cds, 0x2 /* SMTO_ABORTIFHUNG */, 5000, out var r) != IntPtr.Zero && r != IntPtr.Zero)
                        return true;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            Thread.Sleep(50); // 常驻进程可能还在启动
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp(string[] args)
    {
        AppId.Apply(); // 必须在创建任何窗口之前
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9f));
        Store.Load();
        var host = new AppHost(args);
        // 启动后顺手修正任务栏固定项（指向旧路径的话），放到空闲时做，不占用首次显示的时间
        var fixPins = new System.Windows.Forms.Timer { Interval = 3000 };
        fixPins.Tick += (_, _) => { fixPins.Dispose(); AppId.FixPinnedShortcuts(); };
        fixPins.Start();
        Application.Run(host);
    }
}
