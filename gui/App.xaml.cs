using System;
using System.Windows;
using System.Windows.Threading;

namespace WinoxIT
{
    public partial class App : Application
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            LogStartup("App() constructor started.");
        }

        private static void LogStartup(string msg)
        {
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            LogStartup("OnStartup started.");
            try
            {
                base.OnStartup(e);
                LogStartup("base.OnStartup completed.");
            }
            catch (Exception ex)
            {
                LogStartup($"FATAL base.OnStartup: {ex}");
                throw;
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogStartup($"DISPATCHER EXCEPTION: {e.Exception}");
            MessageBox.Show($"Beklenmeyen Arayüz Hatası:\n\n{e.Exception.Message}\n\nİzleme:\n{e.Exception.StackTrace}", 
                            "WinoxIT Hata Bildirimi", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogStartup($"APPDOMAIN UNHANDLED EXCEPTION: {ex}");
                MessageBox.Show($"Kritik Çekirdek Hatası:\n\n{ex.Message}", 
                                "WinoxIT Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
