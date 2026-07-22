using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoClicker.Models;

public enum CompositeStepType { MacroRef, RawEvent }

public class CompositeStep
{
  public CompositeStepType StepType { get; set; }
  public long              DelayMs  { get; set; }

  // StepType == MacroRef 전용 — MacroName은 기본 매크로의 Name을 키로 참조한다
  public string MacroName   { get; set; } = "";
  public int    RepeatCount { get; set; } = 1;

  // StepType == RawEvent 전용 (MacroEvent와 동일한 필드 구성)
  public MacroEventType EventType { get; set; }
  public MouseButton    Button    { get; set; }
  public ClickType      ClickType { get; set; }
  public int            X         { get; set; }
  public int            Y         { get; set; }
  public int             KeyCode  { get; set; }
}

public class CompositeMacro : INotifyPropertyChanged
{
  public Guid              Id          { get; set; } = Guid.NewGuid();
  public DateTime          CreatedAt   { get; set; } = DateTime.Now;
  public int                RepeatCount { get; set; } = 1;
  public List<CompositeStep> Steps     { get; set; } = new();

  private string _name = "새 합성 매크로";
  public string Name
  {
    get => _name;
    set { _name = value; OnPropertyChanged(); }
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? n = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
