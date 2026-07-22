using System.Runtime.InteropServices;
using AutoClicker.Models;

namespace AutoClicker.Services;

public class KeyboardHookService : IDisposable
{
  private readonly RecordingClock _clock;

  private IntPtr _hookHandle = IntPtr.Zero;
  // GC가 수거하지 못하도록 delegate를 인스턴스 필드에 보관
  private NativeMethods.LowLevelKeyboardProc? _proc;

  private readonly HashSet<uint> _pressedKeys = new(); // OS 키 자동반복(auto-repeat) 필터링용
  private bool _isRecording;

  public event Action<MacroEvent>? EventRecorded;

  public KeyboardHookService(RecordingClock clock) => _clock = clock;

  public void StartRecording()
  {
    if (_isRecording) return;
    _isRecording = true;
    _pressedKeys.Clear();

    _proc = HookCallback;
    var hMod = NativeMethods.GetModuleHandle(null);
    _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
  }

  public void StopRecording()
  {
    if (!_isRecording) return;
    _isRecording = false;
    _pressedKeys.Clear();
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
      int msg = (int)wParam;
      var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
      uint vk = data.vkCode;

      // F6(녹화 토글)/F7(재생 토글)은 전역 핫키이므로 매크로 이벤트에서 제외
      if (vk != NativeMethods.VK_F6 && vk != NativeMethods.VK_F7)
      {
        bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
        bool isUp   = msg == NativeMethods.WM_KEYUP   || msg == NativeMethods.WM_SYSKEYUP;

        if (isDown && _pressedKeys.Add(vk)) // 이미 눌려있으면 Add()가 false → 반복 이벤트 무시
          Record(MacroEventType.KeyDown, vk);
        else if (isUp)
        {
          _pressedKeys.Remove(vk);
          Record(MacroEventType.KeyUp, vk);
        }
      }
    }

    return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
  }

  private void Record(MacroEventType type, uint vk)
  {
    var evt = new MacroEvent
    {
      EventType = type,
      KeyCode   = (int)vk,
      DelayMs   = _clock.ElapsedMsSinceLastAndReset()
    };
    EventRecorded?.Invoke(evt);
  }

  public void Dispose() => StopRecording();
}
