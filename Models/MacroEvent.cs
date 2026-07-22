namespace AutoClicker.Models;

public enum MouseButton    { Left, Right }
public enum ClickType      { Single, Double }
public enum MacroEventType { MouseClick, KeyDown, KeyUp, Empty }

public class MacroEvent
{
  public MacroEventType EventType { get; set; } = MacroEventType.MouseClick;

  // 마우스 클릭 전용 (EventType == MouseClick)
  public MouseButton Button    { get; set; }
  public ClickType   ClickType { get; set; }
  public int         X         { get; set; }
  public int         Y         { get; set; }

  // 키보드 전용 (EventType == KeyDown/KeyUp), Win32 가상 키 코드
  public int KeyCode { get; set; }

  public long DelayMs { get; set; }
}
