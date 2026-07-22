using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AutoClicker.Models;
using AutoClicker.Services;
using AutoClicker.ViewModels;

namespace AutoClicker;

public partial class MainWindow : Window
{
  private const int HOTKEY_RECORD = 1;
  private const int HOTKEY_PLAY   = 2;

  private HwndSource? _hwndSource;

  public MainWindow()
  {
    InitializeComponent();
    if (DataContext is MainViewModel vm) WireViewModel(vm);
  }

  private void WireViewModel(MainViewModel vm)
  {
    vm.MacroPickerPrompt = names =>
    {
      var dialog = new MacroPickerDialog(names) { Owner = this };
      return dialog.ShowDialog() == true ? (dialog.SelectedName, dialog.RepeatCount) : (null, 1);
    };
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;
    _hwndSource = HwndSource.FromHwnd(hwnd);
    _hwndSource.AddHook(WndProc);

    NativeMethods.RegisterHotKey(hwnd, HOTKEY_RECORD, 0, NativeMethods.VK_F6);
    NativeMethods.RegisterHotKey(hwnd, HOTKEY_PLAY,   0, NativeMethods.VK_F7);
  }

  private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if (msg == NativeMethods.WM_HOTKEY && DataContext is MainViewModel vm)
    {
      switch (wParam.ToInt32())
      {
        case HOTKEY_RECORD:
          vm.ToggleRecordCommand.Execute(null);
          handled = true;
          break;
        case HOTKEY_PLAY:
          vm.TogglePlayCommand.Execute(null);
          handled = true;
          break;
      }
    }
    return IntPtr.Zero;
  }

  private void MacroListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (DataContext is MainViewModel vm && sender is ListBox lb)
    {
      vm.SetSelectedMacros(lb.SelectedItems.Cast<Macro>());
      if (lb.SelectedItems.Count == 1)
        vm.SelectedMacro = (Macro)lb.SelectedItems[0]!;
    }
  }

  private void CompositeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (DataContext is MainViewModel vm && sender is ListBox lb)
    {
      vm.SetSelectedComposites(lb.SelectedItems.Cast<CompositeMacro>());
      if (lb.SelectedItems.Count == 1)
        vm.SelectedComposite = (CompositeMacro)lb.SelectedItems[0]!;
    }
  }

  protected override void OnClosed(EventArgs e)
  {
    if (DataContext is MainViewModel vm) vm.SaveOnExit();

    var hwnd = new WindowInteropHelper(this).Handle;
    NativeMethods.UnregisterHotKey(hwnd, HOTKEY_RECORD);
    NativeMethods.UnregisterHotKey(hwnd, HOTKEY_PLAY);
    _hwndSource?.RemoveHook(WndProc);
    base.OnClosed(e);
  }
}
