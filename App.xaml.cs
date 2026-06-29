using System.Windows;

namespace AutoClicker;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    DispatcherUnhandledException += (_, ex) =>
    {
      MessageBox.Show($"오류: {ex.Exception.Message}", "AutoClicker",
        MessageBoxButton.OK, MessageBoxImage.Error);
      ex.Handled = true;
    };
  }
}
