using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace SystemMonitor
{
    public static class Program
    {
        static int errorDialogs;

        internal static void ShowError(string where, Exception ex)
        {
            Log.Write(where + ": " + ex);
            if (Interlocked.Increment(ref errorDialogs) > 3) return;   // не засипаємо вікнами
            try
            {
                MessageBox.Show(where + ":\n\n" + ex.Message + "\n\nПовна помилка записана в журнал:\n" + Log.FilePath,
                    "Системний монітор: помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            bool restarted = Array.IndexOf(args, "--restarted") >= 0;
            using (Mutex mutex = new Mutex(false, "SystemMonitorGlass_v2"))
            {
                bool owned;
                try { owned = mutex.WaitOne(restarted ? 20000 : 0); }
                catch (AbandonedMutexException) { owned = true; }   // попередній екземпляр завершився, не відпустивши м'ютекс
                if (!owned) return;   // уже запущено

                Log.Init();
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                AppCore.SafeStart = Array.IndexOf(args, "--safe") >= 0;

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    ShowError("Необроблена помилка", e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    ShowError("Критична помилка", ex ?? new Exception(Convert.ToString(e.ExceptionObject)));
                };
                app.Startup += delegate { AppCore.Start(); };
                app.Run();
            }

        }
    }
}
