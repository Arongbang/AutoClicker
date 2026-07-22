using System.Diagnostics;

namespace AutoClicker.Services;

// 마우스/키보드 등 여러 후킹 서비스가 하나의 매크로를 녹화할 때
// 이벤트 간 DelayMs를 전역적으로 일관되게 계산하기 위한 공유 타이머.
public class RecordingClock
{
  private long _lastTimestamp;

  public void Reset() => _lastTimestamp = Stopwatch.GetTimestamp();

  public long ElapsedMsSinceLastAndReset()
  {
    var now = Stopwatch.GetTimestamp();
    long delayMs = (long)((now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency);
    _lastTimestamp = now;
    return delayMs;
  }
}
