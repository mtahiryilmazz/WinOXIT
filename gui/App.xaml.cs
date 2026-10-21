using System;
using System.Windows;
using System.Windows.Threading;

namespace WinoxIT
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Beklenmeyen Arayüz Hatası:\n\n{e.Exception.Message}\n\nİzleme:\n{e.Exception.StackTrace}", 
                            "WinoxIT Hata Bildirimi", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Kritik Çekirdek Hatası:\n\n{ex.Message}", 
                                "WinoxIT Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
