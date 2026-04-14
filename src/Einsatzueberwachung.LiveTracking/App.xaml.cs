using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace Einsatzueberwachung.LiveTracking
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Unbehandelter Fehler:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
