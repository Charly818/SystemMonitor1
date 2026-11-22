using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace SystemMonitor
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "SystemMonitorGlass_v2", out created))
            {
                if (!created) return;   // уже запущено

                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Write("необроблена помилка: " + e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("критична помилка: " + e.ExceptionObject);
                };
                app.Startup += delegate { AppCore.Start(); };
                app.Run();
            }
        }
    }
}
