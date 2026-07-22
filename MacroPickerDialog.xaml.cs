using System.Windows;

namespace AutoClicker;

public partial class MacroPickerDialog : Window
{
  public string? SelectedName { get; private set; }
  public int      RepeatCount  { get; private set; } = 1;

  public MacroPickerDialog(IEnumerable<string> macroNames)
  {
    InitializeComponent();
    foreach (var name in macroNames) MacroListBox.Items.Add(name);
    if (MacroListBox.Items.Count > 0) MacroListBox.SelectedIndex = 0;
  }

  private void OnAdd(object sender, RoutedEventArgs e)
  {
    if (MacroListBox.SelectedItem is not string name)
    {
      MessageBox.Show("매크로를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }
    if (!int.TryParse(RepeatBox.Text, out int n) || n < 1)
    {
      MessageBox.Show("반복 횟수는 1 이상의 정수여야 합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }
    SelectedName = name;
    RepeatCount  = n;
    DialogResult = true;
  }
}
