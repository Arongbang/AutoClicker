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

  // 녹화가 끝난 뒤 사용자가 직접 입력하는 설명. 녹화 중에는 항상 빈 값이다.
  public string Description { get; set; } = "";
}
