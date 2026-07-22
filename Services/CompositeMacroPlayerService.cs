using AutoClicker.Models;

namespace AutoClicker.Services;

// 합성 매크로가 참조하는 기본 매크로 중 하나 이상을 찾을 수 없을 때 던진다
public class MissingMacroException : Exception
{
  public IReadOnlyList<string> MissingNames { get; }

  public MissingMacroException(IReadOnlyList<string> missingNames)
    : base($"다음 매크로를 찾을 수 없습니다: {string.Join(", ", missingNames)}")
  {
    MissingNames = missingNames;
  }
}

public class CompositeMacroPlayerService
{
  public async Task<int> PlayAsync(
    CompositeMacro composite,
    Func<string, Macro?> resolveMacro,
    CancellationToken ct,
    IProgress<int>? progress = null)
  {
    // 참조된 기본 매크로를 재생 시작 전에 전부 해석해 둔다 (규칙 3: 누락 시 실행 자체를 막는다)
    var resolved = new Dictionary<string, Macro>();
    var missing  = new List<string>();
    foreach (var step in composite.Steps)
    {
      if (step.StepType != CompositeStepType.MacroRef || resolved.ContainsKey(step.MacroName)) continue;
      var macro = resolveMacro(step.MacroName);
      if (macro == null) missing.Add(step.MacroName);
      else resolved[step.MacroName] = macro;
    }
    if (missing.Count > 0) throw new MissingMacroException(missing);

    int totalRuns      = composite.RepeatCount == 0 ? int.MaxValue : composite.RepeatCount;
    int completedRuns  = 0;

    for (int run = 0; run < totalRuns && !ct.IsCancellationRequested; run++)
    {
      progress?.Report(run + 1);
      foreach (var step in composite.Steps)
      {
        ct.ThrowIfCancellationRequested();

        if (step.DelayMs > 0)
          await Task.Delay(TimeSpan.FromMilliseconds(step.DelayMs), ct).ConfigureAwait(false);

        if (step.StepType == CompositeStepType.MacroRef)
        {
          var baseMacro = resolved[step.MacroName];
          // 기본 매크로 자체의 반복 설정도 곱해서 실행한다. 무한 반복(0)으로 설정된 매크로는 1회로 취급한다.
          int baseRepeat = baseMacro.RepeatCount == 0 ? 1 : baseMacro.RepeatCount;
          int totalRepeat = step.RepeatCount * baseRepeat;
          for (int i = 0; i < totalRepeat; i++)
          {
            ct.ThrowIfCancellationRequested();
            await MacroPlayerService.PlayEventsOnceAsync(baseMacro.Events, ct).ConfigureAwait(false);
          }
        }
        else
        {
          var evt = new MacroEvent
          {
            EventType = step.EventType, Button = step.Button, ClickType = step.ClickType,
            X = step.X, Y = step.Y, KeyCode = step.KeyCode, DelayMs = 0
          };
          await MacroPlayerService.PlayEventsOnceAsync(new[] { evt }, ct).ConfigureAwait(false);
        }
      }
      completedRuns++;
    }

    return completedRuns;
  }

  // 합성 매크로 1회 실행 예상 소요시간(ms) — 참조된 기본 매크로를 찾을 수 없으면 해당 스텝은 0으로 계산한다
  public static long EstimateDurationMs(CompositeMacro composite, Func<string, Macro?> resolveMacro)
  {
    long total = 0;
    foreach (var step in composite.Steps)
    {
      total += step.DelayMs;
      if (step.StepType == CompositeStepType.MacroRef)
      {
        var baseMacro = resolveMacro(step.MacroName);
        if (baseMacro != null)
        {
          // 기본 매크로 자체의 반복 설정도 곱해서 계산한다. 무한 반복(0)으로 설정된 매크로는 1회로 취급한다.
          int baseRepeat = baseMacro.RepeatCount == 0 ? 1 : baseMacro.RepeatCount;
          total += baseMacro.Events.Sum(e => e.DelayMs) * step.RepeatCount * baseRepeat;
        }
      }
    }
    return total;
  }
}
