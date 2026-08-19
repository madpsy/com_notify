using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("ComNotify")]
[assembly: AssemblyDescription("Tray notifier for USB serial (COM port) adapters")]
[assembly: AssemblyProduct("ComNotify")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ComNotify
{
    internal static class Program
    {
        private const string MutexName = "ComNotify_SingleInstance_9F31";
        public const string ShowEventName = "ComNotify_ShowWindow_9F31";

        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: ask the live instance to surface its port list, then bow out.
                    EventWaitHandle signal;
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out signal))
                    {
                        using (signal) signal.Set();
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Report(e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Report(e.ExceptionObject as Exception);
                };

                Application.Run(new TrayContext());
                GC.KeepAlive(mutex);
            }
        }

        private static void Report(Exception ex)
        {
            if (ex == null) return;
            MessageBox.Show("ComNotify hit an unexpected error:\r\n\r\n" + ex,
                "ComNotify", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
