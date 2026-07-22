using System.Runtime.InteropServices;
using AutoClicker.Models;

namespace AutoClicker.Services;

public class MouseHookService : IDisposable
{
  private readonly RecordingClock _clock;

  private IntPtr _hookHandle = IntPtr.Zero;
  // GC가 수거하지 못하도록 delegate를 인스턴스 필드에 보관
  private NativeMethods.LowLevelMouseProc? _proc;

  private bool _lastWasSingleLeft;
  private bool _lastWasSingleRight;
  private bool _isRecording;

  public event Action<MacroEvent>?            EventRecorded;
  public event Action<ClickType, Models.MouseButton>? LastEventReplace;

  public MouseHookService(RecordingClock clock) => _clock = clock;

  public void StartRecording()
  {
    if (_isRecording) return;
    _isRecording = true;
    _lastWasSingleLeft = false;
    _lastWasSingleRight = false;

    _proc = HookCallback;
    var hMod = NativeMethods.GetModuleHandle(null);
    _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
  }

  public void StopRecording()
  {
    if (!_isRecording) return;
    _isRecording = false;
    if (_hookHandle != IntPtr.Zero)
    {
      NativeMethods.UnhookWindowsHookEx(_hookHandle);
      _hookHandle = IntPtr.Zero;
    }
  }

  private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
  {
    if (nCode >= 0 && _isRecording)
    {
      var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
      int msg = (int)wParam;

      switch (msg)
      {
        case NativeMethods.WM_LBUTTONDOWN:
          var leftEvt = new MacroEvent
          {
            Button    = Models.MouseButton.Left,
            ClickType = ClickType.Single,
            X         = data.pt.X,
            Y         = data.pt.Y,
            DelayMs   = _clock.ElapsedMsSinceLastAndReset()
          };
          EventRecorded?.Invoke(leftEvt);
          _lastWasSingleLeft  = true;
          _lastWasSingleRight = false;
          break;

        case NativeMethods.WM_LBUTTONDBLCLK:
          // 직전에 기록된 단일 왼쪽 클릭을 더블클릭으로 교체
          if (_lastWasSingleLeft)
            LastEventReplace?.Invoke(ClickType.Double, Models.MouseButton.Left);
          _lastWasSingleLeft = false;
          break;

        case NativeMethods.WM_RBUTTONDOWN:
          var rightEvt = new MacroEvent
          {
            Button    = Models.MouseButton.Right,
            ClickType = ClickType.Single,
            X         = data.pt.X,
            Y         = data.pt.Y,
            DelayMs   = _clock.ElapsedMsSinceLastAndReset()
          };
          EventRecorded?.Invoke(rightEvt);
          _lastWasSingleRight = true;
          _lastWasSingleLeft  = false;
          break;

        case NativeMethods.WM_RBUTTONDBLCLK:
          if (_lastWasSingleRight)
            LastEventReplace?.Invoke(ClickType.Double, Models.MouseButton.Right);
          _lastWasSingleRight = false;
          break;
      }
    }

    return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
  }

  public void Dispose() => StopRecording();
}
