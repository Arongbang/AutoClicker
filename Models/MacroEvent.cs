namespace AutoClicker.Models;

public enum MouseButton { Left, Right }
public enum ClickType   { Single, Double }

public class MacroEvent
{
  public MouseButton Button    { get; set; }
  public ClickType   ClickType { get; set; }
  public int         X         { get; set; }
  public int         Y         { get; set; }
  public long        DelayMs   { get; set; }
}
