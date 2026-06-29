using System.Runtime.InteropServices;

namespace AutoClicker.Services;

internal static class NativeMethods
{
  // 훅 타입
  public const int WH_MOUSE_LL = 14;

  // 마우스 메시지
  public const int WM_LBUTTONDOWN  = 0x0201;
  public const int WM_RBUTTONDOWN  = 0x0204;
  public const int WM_LBUTTONDBLCLK = 0x0203;
  public const int WM_RBUTTONDBLCLK = 0x0206;

  // SendInput 플래그
  public const uint MOUSEEVENTF_MOVE       = 0x0001;
  public const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
  public const uint MOUSEEVENTF_LEFTUP     = 0x0004;
  public const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
  public const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
  public const uint MOUSEEVENTF_ABSOLUTE   = 0x8000;
  public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

  // 핫키
  public const int  WM_HOTKEY = 0x0312;
  public const uint VK_F6     = 0x75;
  public const uint VK_F7     = 0x76;

  // GetSystemMetrics 인덱스 (가상 데스크탑)
  public const int SM_XVIRTUALSCREEN  = 76;
  public const int SM_YVIRTUALSCREEN  = 77;
  public const int SM_CXVIRTUALSCREEN = 78;
  public const int SM_CYVIRTUALSCREEN = 79;

  // INPUT type
  public const uint INPUT_MOUSE = 0;

  public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll", SetLastError = true)]
  public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

  [DllImport("user32.dll", SetLastError = true)]
  public static extern bool UnhookWindowsHookEx(IntPtr hhk);

  [DllImport("user32.dll")]
  public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

  [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
  public static extern IntPtr GetModuleHandle(string? lpModuleName);

  [DllImport("user32.dll", SetLastError = true)]
  public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [DllImport("user32.dll")]
  public static extern bool GetCursorPos(out POINT lpPoint);

  [DllImport("user32.dll")]
  public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

  [DllImport("user32.dll")]
  public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

  [DllImport("user32.dll")]
  public static extern int GetSystemMetrics(int nIndex);

  [StructLayout(LayoutKind.Sequential)]
  public struct POINT
  {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct MSLLHOOKSTRUCT
  {
    public POINT   pt;
    public uint    mouseData;
    public uint    flags;
    public uint    time;
    public IntPtr  dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct INPUT
  {
    public uint      type;
    public InputUnion u;
  }

  [StructLayout(LayoutKind.Explicit)]
  public struct InputUnion
  {
    [FieldOffset(0)] public MOUSEINPUT    mi;
    [FieldOffset(0)] public KEYBDINPUT    ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct MOUSEINPUT
  {
    public int    dx;
    public int    dy;
    public uint   mouseData;
    public uint   dwFlags;
    public uint   time;
    public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct KEYBDINPUT
  {
    public ushort wVk;
    public ushort wScan;
    public uint   dwFlags;
    public uint   time;
    public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct HARDWAREINPUT
  {
    public uint   uMsg;
    public ushort wParamL;
    public ushort wParamH;
  }
}
