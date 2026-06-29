using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoClicker.Models;

public class Macro : INotifyPropertyChanged
{
  public Guid             Id        { get; set; } = Guid.NewGuid();
  public DateTime         CreatedAt { get; set; } = DateTime.Now;
  public int              RepeatCount { get; set; } = 1;
  public List<MacroEvent> Events    { get; set; } = new();

  private string _name = "새 매크로";
  public string Name
  {
    get => _name;
    set { _name = value; OnPropertyChanged(); }
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? n = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
