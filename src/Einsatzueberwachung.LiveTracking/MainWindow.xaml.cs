using System.Windows;

namespace Einsatzueberwachung.LiveTracking
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.LoadSettings();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }
    }
}
