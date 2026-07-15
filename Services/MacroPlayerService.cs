using System.Runtime.InteropServices;
using AutoClicker.Models;

namespace AutoClicker.Services;

public class MacroPlayerService
{
  public async Task<int> PlayAsync(Macro macro, CancellationToken ct, IProgress<int>? progress = null)
  {
    int vLeft   = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
    int vTop    = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
    int vWidth  = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
    int vHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

    int totalRuns   = macro.RepeatCount == 0 ? int.MaxValue : macro.RepeatCount;
    int completedRuns = 0;

    for (int run = 0; run < totalRuns && !ct.IsCancellationRequested; run++)
    {
      progress?.Report(run + 1);
      foreach (var evt in macro.Events)
      {
        ct.ThrowIfCancellationRequested();

        if (evt.DelayMs > 0)
          await Task.Delay(TimeSpan.FromMilliseconds(evt.DelayMs), ct);

        // 물리 픽셀 → SendInput 정규화 좌표 (0~65535)
        int nx = (int)((long)(evt.X - vLeft) * 65535 / vWidth);
        int ny = (int)((long)(evt.Y - vTop)  * 65535 / vHeight);

        SendClick(evt.Button, evt.ClickType, nx, ny);
      }
      completedRuns++;
    }

    return completedRuns;
  }

  private static void SendClick(Models.MouseButton button, ClickType type, int nx, int ny)
  {
    uint downFlag = button == Models.MouseButton.Left
      ? NativeMethods.MOUSEEVENTF_LEFTDOWN
      : NativeMethods.MOUSEEVENTF_RIGHTDOWN;
    uint upFlag = button == Models.MouseButton.Left
      ? NativeMethods.MOUSEEVENTF_LEFTUP
      : NativeMethods.MOUSEEVENTF_RIGHTUP;

    uint moveFlags = NativeMethods.MOUSEEVENTF_MOVE
                   | NativeMethods.MOUSEEVENTF_ABSOLUTE
                   | NativeMethods.MOUSEEVENTF_VIRTUALDESK;

    NativeMethods.INPUT[] inputs;

    if (type == ClickType.Single)
    {
      inputs = new[]
      {
        MakeMouseInput(nx, ny, moveFlags),
        MakeMouseInput(0, 0, downFlag),
        MakeMouseInput(0, 0, upFlag),
      };
    }
    else
    {
      inputs = new[]
      {
        MakeMouseInput(nx, ny, moveFlags),
        MakeMouseInput(0, 0, downFlag),
        MakeMouseInput(0, 0, upFlag),
        MakeMouseInput(0, 0, downFlag),
        MakeMouseInput(0, 0, upFlag),
      };
    }

    NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
  }

  private static NativeMethods.INPUT MakeMouseInput(int dx, int dy, uint flags)
  {
    return new NativeMethods.INPUT
    {
      type = NativeMethods.INPUT_MOUSE,
      u = new NativeMethods.InputUnion
      {
        mi = new NativeMethods.MOUSEINPUT
        {
          dx       = dx,
          dy       = dy,
          dwFlags  = flags,
          mouseData = 0,
          time     = 0,
          dwExtraInfo = IntPtr.Zero
        }
      }
    };
  }
}
