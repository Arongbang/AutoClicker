using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using AutoClicker.Models;
using AutoClicker.Services;

namespace AutoClicker.ViewModels;

// 이벤트 목록 UI에서 공통으로 쓰는 지연시간 표시/파싱, 키 이름 표시 로직
internal static class EventDisplay
{
  // 지연시간을 가장 자연스러운 단위(h/m/s/ms)로 표시한다.
  public static string FormatDelay(long ms)
  {
    if (ms != 0 && ms % 3_600_000 == 0) return $"{ms / 3_600_000}h";
    if (ms != 0 && ms % 60_000    == 0) return $"{ms / 60_000}m";
    if (ms != 0 && ms % 1_000     == 0) return $"{ms / 1_000}s";
    return $"{ms}ms";
  }

  // "500", "500ms", "1.5s", "2m", "1h" 등 ms/s/m/h 단위 입력을 밀리초로 변환한다.
  public static bool TryParseDelay(string? text, out long ms)
  {
    ms = 0;
    if (string.IsNullOrWhiteSpace(text)) return false;

    var match = Regex.Match(text.Trim(), @"^([0-9]*\.?[0-9]+)\s*(ms|s|m|h)?$", RegexOptions.IgnoreCase);
    if (!match.Success) return false;
    if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
      return false;

    var unitMs = match.Groups[2].Value.ToLowerInvariant() switch
    {
      "h" => 3_600_000d,
      "m" => 60_000d,
      "s" => 1_000d,
      _   => 1d,
    };

    ms = (long)Math.Round(value * unitMs);
    return true;
  }

  public static string KeyName(int vk) => KeyInterop.KeyFromVirtualKey(vk).ToString();
}

public class MacroEventRow : INotifyPropertyChanged
{
  private readonly MacroEvent _source;

  public int    Number { get; }
  public string Kind   { get; }
  public int    X      { get; }
  public int    Y      { get; }

  public string Delay
  {
    get => EventDisplay.FormatDelay(_source.DelayMs);
    set
    {
      if (!EventDisplay.TryParseDelay(value, out var ms) || ms < 0) return;
      _source.DelayMs = ms;
      OnPropertyChanged();
    }
  }

  public MacroEventRow(MacroEvent e, int number)
  {
    _source = e;
    Number  = number;
    Kind    = e.EventType switch
    {
      MacroEventType.MouseClick => $"{e.Button} {(e.ClickType == ClickType.Double ? "더블클릭" : "클릭")}",
      MacroEventType.KeyDown    => $"{EventDisplay.KeyName(e.KeyCode)} 누름",
      MacroEventType.KeyUp      => $"{EventDisplay.KeyName(e.KeyCode)} 뗌",
      _                         => "?",
    };
    X       = e.X;
    Y       = e.Y;
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? n = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class CompositeStepRow : INotifyPropertyChanged
{
  internal readonly CompositeStep Source;

  public int    Number { get; }
  public string Kind   { get; private set; }

  public string Delay
  {
    get => EventDisplay.FormatDelay(Source.DelayMs);
    set
    {
      if (!EventDisplay.TryParseDelay(value, out var ms) || ms < 0) return;
      Source.DelayMs = ms;
      OnPropertyChanged();
    }
  }

  public CompositeStepRow(CompositeStep s, int number, Func<string, Macro?> resolveMacro)
  {
    Source = s;
    Number = number;
    Kind   = BuildKind(s, resolveMacro);
  }

  // 매크로 목록이 바뀌었을 수 있으므로(삭제/재생성) Kind 텍스트를 다시 계산한다.
  public void Refresh(Func<string, Macro?> resolveMacro)
  {
    Kind = BuildKind(Source, resolveMacro);
    OnPropertyChanged(nameof(Kind));
  }

  private static string BuildKind(CompositeStep s, Func<string, Macro?> resolveMacro)
  {
    if (s.StepType == CompositeStepType.MacroRef)
    {
      bool missing = resolveMacro(s.MacroName) == null;
      return $"{s.MacroName} × {s.RepeatCount}{(missing ? " (누락됨)" : "")}";
    }
    return s.EventType switch
    {
      MacroEventType.MouseClick => $"{s.Button} {(s.ClickType == ClickType.Double ? "더블클릭" : "클릭")}",
      MacroEventType.KeyDown    => $"{EventDisplay.KeyName(s.KeyCode)} 누름",
      MacroEventType.KeyUp      => $"{EventDisplay.KeyName(s.KeyCode)} 뗌",
      _                         => "?",
    };
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? n = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public enum AppState { Idle, Recording, Playing }

public class MainViewModel : INotifyPropertyChanged
{
  private readonly RecordingClock      _recordingClock;
  private readonly MouseHookService    _hookService;
  private readonly KeyboardHookService _keyboardHookService;
  private readonly MacroPlayerService  _playerService  = new();
  private readonly MacroStorageService _storageService = new();
  private readonly CompositeMacroPlayerService  _compositePlayerService  = new();
  private readonly CompositeMacroStorageService _compositeStorageService = new();

  private AppState _state = AppState.Idle;
  private CancellationTokenSource? _playCts;

  private bool _isCapturingStep;
  private uint _capturePendingKeyDownVk;
  private CompositeStep? _captureMouseStep;
  private readonly System.Windows.Threading.DispatcherTimer _captureFinalizeTimer =
    new() { Interval = TimeSpan.FromMilliseconds(500) };

  public ObservableCollection<Macro>        Macros        { get; } = new();
  public ObservableCollection<MacroEventRow> CurrentEvents { get; } = new();

  public ObservableCollection<CompositeMacro>     CompositeMacros { get; } = new();
  public ObservableCollection<CompositeStepRow>   CurrentSteps    { get; } = new();

  private int _activeTabIndex;
  public int ActiveTabIndex
  {
    get => _activeTabIndex;
    set { _activeTabIndex = value; OnPropertyChanged(); RaiseAllCommands(); }
  }

  private readonly List<Macro> _selectedMacros = new();
  private Macro? _selectedMacro;
  public Macro? SelectedMacro
  {
    get => _selectedMacro;
    set
    {
      if (_state != AppState.Idle) return;
      _selectedMacro = value;
      OnPropertyChanged();
      OnPropertyChanged(nameof(MacroName));
      OnPropertyChanged(nameof(RepeatCountText));
      OnPropertyChanged(nameof(IsInfiniteRepeat));
      OnPropertyChanged(nameof(EstimatedDurationText));
      RefreshEventList();
      RaiseAllCommands();
    }
  }

  public string MacroName
  {
    get => _selectedMacro?.Name ?? "";
    set
    {
      if (_selectedMacro == null) return;
      if (value != _selectedMacro.Name && Macros.Any(m => m != _selectedMacro && m.Name == value))
      {
        MessageBox.Show("이미 사용 중인 매크로 이름입니다.", "이름 중복", MessageBoxButton.OK, MessageBoxImage.Warning);
        OnPropertyChanged(); // 이전 이름으로 되돌리기 위해 TextBox 바인딩을 다시 읽게 한다
        return;
      }
      _selectedMacro.Name = value;  // Macro.INotifyPropertyChanged가 ListBox를 자동 갱신
      OnPropertyChanged();
      _ = SaveAsync();
    }
  }

  public string RepeatCountText
  {
    get => _selectedMacro?.RepeatCount.ToString() ?? "1";
    set
    {
      if (_selectedMacro == null) return;
      if (int.TryParse(value, out int n) && n >= 1)
      {
        _selectedMacro.RepeatCount = n;
        _ = SaveAsync();
      }
      OnPropertyChanged();
    }
  }

  public bool IsInfiniteRepeat
  {
    get => _selectedMacro?.RepeatCount == 0;
    set
    {
      if (_selectedMacro == null) return;
      _selectedMacro.RepeatCount = value ? 0 : 1;
      OnPropertyChanged();
      OnPropertyChanged(nameof(RepeatCountText));
      OnPropertyChanged(nameof(IsRepeatCountEnabled));
      _ = SaveAsync();
    }
  }

  public bool IsRepeatCountEnabled => !IsInfiniteRepeat;

  public string EstimatedDurationText =>
    FormatDuration(_selectedMacro?.Events.Sum(e => e.DelayMs) ?? 0);

  private readonly List<CompositeMacro> _selectedComposites = new();
  private CompositeMacro? _selectedComposite;
  public CompositeMacro? SelectedComposite
  {
    get => _selectedComposite;
    set
    {
      if (_state != AppState.Idle) return;
      _selectedComposite = value;
      OnPropertyChanged();
      OnPropertyChanged(nameof(CompositeName));
      OnPropertyChanged(nameof(CompositeRepeatCountText));
      OnPropertyChanged(nameof(CompositeIsInfiniteRepeat));
      OnPropertyChanged(nameof(CompositeEstimatedDurationText));
      RefreshStepList();
      RaiseAllCommands();
    }
  }

  private CompositeStepRow? _selectedStepRow;
  public CompositeStepRow? SelectedStepRow
  {
    get => _selectedStepRow;
    set { _selectedStepRow = value; RaiseAllCommands(); }
  }

  public string CompositeName
  {
    get => _selectedComposite?.Name ?? "";
    set
    {
      if (_selectedComposite == null) return;
      _selectedComposite.Name = value;
      OnPropertyChanged();
      _ = SaveCompositesAsync();
    }
  }

  public string CompositeRepeatCountText
  {
    get => _selectedComposite?.RepeatCount.ToString() ?? "1";
    set
    {
      if (_selectedComposite == null) return;
      if (int.TryParse(value, out int n) && n >= 1)
      {
        _selectedComposite.RepeatCount = n;
        _ = SaveCompositesAsync();
      }
      OnPropertyChanged();
    }
  }

  public bool CompositeIsInfiniteRepeat
  {
    get => _selectedComposite?.RepeatCount == 0;
    set
    {
      if (_selectedComposite == null) return;
      _selectedComposite.RepeatCount = value ? 0 : 1;
      OnPropertyChanged();
      OnPropertyChanged(nameof(CompositeRepeatCountText));
      OnPropertyChanged(nameof(CompositeIsRepeatCountEnabled));
      _ = SaveCompositesAsync();
    }
  }

  public bool CompositeIsRepeatCountEnabled => !CompositeIsInfiniteRepeat;

  public string CompositeEstimatedDurationText => FormatDuration(
    _selectedComposite == null ? 0 : CompositeMacroPlayerService.EstimateDurationMs(_selectedComposite, ResolveMacroByName));

  // 매크로 1회 실행 예상 소요시간을 사람이 읽기 쉬운 형태로 변환한다.
  private static string FormatDuration(long ms)
  {
    var ts = TimeSpan.FromMilliseconds(ms);
    if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
    if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
    return $"{ts.TotalSeconds:0.#}s";
  }

  // 재생 중 매크로 1회당 남은 시간을 1초 단위로 표시한다.
  private static string FormatRemaining(long ms)
  {
    long totalSeconds = (long)Math.Ceiling(ms / 1000.0);
    var ts = TimeSpan.FromSeconds(totalSeconds);
    if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
    if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
    return $"{ts.Seconds}s";
  }

  private string _statusText = "대기 중";
  public string StatusText
  {
    get => _statusText;
    private set { _statusText = value; OnPropertyChanged(); }
  }

  private string _remainingTimeText = "";
  public string RemainingTimeText
  {
    get => _remainingTimeText;
    private set { _remainingTimeText = value; OnPropertyChanged(); }
  }

  private readonly System.Windows.Threading.DispatcherTimer _countdownTimer =
    new() { Interval = TimeSpan.FromSeconds(1) };
  private long _remainingMsInRun;

  // 커맨드
  public ICommand RecordCommand  { get; }
  public ICommand StopCommand    { get; }
  public ICommand PlayCommand    { get; }
  public ICommand NewMacroCommand  { get; }
  public ICommand DeleteCommand  { get; }
  public ICommand DuplicateCommand { get; }
  public ICommand MoveMacroUpCommand   { get; }
  public ICommand MoveMacroDownCommand { get; }
  public ICommand ToggleRecordCommand { get; }
  public ICommand TogglePlayCommand   { get; }

  public ICommand NewCompositeCommand      { get; }
  public ICommand DeleteCompositeCommand   { get; }
  public ICommand DuplicateCompositeCommand{ get; }
  public ICommand MoveCompositeUpCommand   { get; }
  public ICommand MoveCompositeDownCommand { get; }
  public ICommand PlayCompositeCommand     { get; }
  public ICommand AddMacroRefStepCommand   { get; }
  public ICommand CaptureStepCommand       { get; }
  public ICommand RemoveStepCommand        { get; }
  public ICommand MoveStepUpCommand        { get; }
  public ICommand MoveStepDownCommand      { get; }

  public Func<IEnumerable<string>, (string? name, int repeatCount)>? MacroPickerPrompt { get; set; }

  public MainViewModel()
  {
    _recordingClock      = new RecordingClock();
    _hookService         = new MouseHookService(_recordingClock);
    _keyboardHookService = new KeyboardHookService(_recordingClock);

    RecordCommand   = new RelayCommand(_ => StartRecording(),  _ => _state == AppState.Idle && _selectedMacro != null);
    StopCommand     = new RelayCommand(_ => Stop(removeLastEvent: true), _ => _state != AppState.Idle);
    PlayCommand     = new AsyncRelayCommand(_ => StartPlayingAsync(), _ => _state == AppState.Idle && _selectedMacro?.Events.Count > 0);
    NewMacroCommand = new RelayCommand(_ => CreateNewMacro(), _ => _state == AppState.Idle);
    DeleteCommand   = new RelayCommand(_ => DeleteSelected(),   _ => _state == AppState.Idle && _selectedMacros.Count > 0);
    DuplicateCommand = new RelayCommand(_ => DuplicateSelected(), _ => _state == AppState.Idle && _selectedMacros.Count > 0);
    MoveMacroUpCommand   = new RelayCommand(_ => MoveMacro(-1), _ => _state == AppState.Idle && _selectedMacro != null && Macros.IndexOf(_selectedMacro) > 0);
    MoveMacroDownCommand = new RelayCommand(_ => MoveMacro(1),  _ => _state == AppState.Idle && _selectedMacro != null && Macros.IndexOf(_selectedMacro) >= 0 && Macros.IndexOf(_selectedMacro) < Macros.Count - 1);

    ToggleRecordCommand = new RelayCommand(_ =>
    {
      if (_state == AppState.Recording) Stop();
      else if (_state == AppState.Idle && ActiveTabIndex == 0 && _selectedMacro != null) StartRecording();
    });
    TogglePlayCommand = new RelayCommand(_ =>
    {
      if (_state == AppState.Playing) Stop();
      else if (_state == AppState.Idle && ActiveTabIndex == 0 && _selectedMacro?.Events.Count > 0)
        _ = StartPlayingAsync();
      else if (_state == AppState.Idle && ActiveTabIndex == 1 && _selectedComposite?.Steps.Count > 0)
        _ = StartPlayingCompositeAsync();
    });

    NewCompositeCommand       = new RelayCommand(_ => CreateNewComposite(), _ => _state == AppState.Idle);
    DeleteCompositeCommand    = new RelayCommand(_ => DeleteSelectedComposites(), _ => _state == AppState.Idle && _selectedComposites.Count > 0);
    DuplicateCompositeCommand = new RelayCommand(_ => DuplicateSelectedComposites(), _ => _state == AppState.Idle && _selectedComposites.Count > 0);
    MoveCompositeUpCommand    = new RelayCommand(_ => MoveComposite(-1), _ => _state == AppState.Idle && _selectedComposite != null && CompositeMacros.IndexOf(_selectedComposite) > 0);
    MoveCompositeDownCommand  = new RelayCommand(_ => MoveComposite(1),  _ => _state == AppState.Idle && _selectedComposite != null && CompositeMacros.IndexOf(_selectedComposite) >= 0 && CompositeMacros.IndexOf(_selectedComposite) < CompositeMacros.Count - 1);
    PlayCompositeCommand      = new AsyncRelayCommand(_ => StartPlayingCompositeAsync(), _ => _state == AppState.Idle && _selectedComposite?.Steps.Count > 0);
    AddMacroRefStepCommand    = new RelayCommand(_ => AddMacroRefStep(), _ => _state == AppState.Idle && _selectedComposite != null && Macros.Count > 0);
    CaptureStepCommand        = new RelayCommand(_ =>
    {
      if (_isCapturingStep) CancelCaptureStep();
      else StartCaptureStep();
    }, _ => _isCapturingStep || (_state == AppState.Idle && _selectedComposite != null));
    RemoveStepCommand  = new RelayCommand(_ => RemoveSelectedStep(), _ => _state == AppState.Idle && _selectedComposite != null && _selectedStepRow != null);
    MoveStepUpCommand   = new RelayCommand(_ => MoveStep(-1), _ => _state == AppState.Idle && _selectedComposite != null && _selectedStepRow != null && _selectedComposite.Steps.IndexOf(_selectedStepRow.Source) > 0);
    MoveStepDownCommand = new RelayCommand(_ => MoveStep(1),  _ => _state == AppState.Idle && _selectedComposite != null && _selectedStepRow != null && _selectedComposite.Steps.IndexOf(_selectedStepRow.Source) < _selectedComposite.Steps.Count - 1);

    _hookService.EventRecorded   += OnEventRecorded;
    _hookService.LastEventReplace += OnLastEventReplace;
    _keyboardHookService.EventRecorded += OnEventRecorded;
    _countdownTimer.Tick += (_, __) =>
    {
      _remainingMsInRun = Math.Max(0, _remainingMsInRun - 1000);
      RemainingTimeText = $"남은 시간: {FormatRemaining(_remainingMsInRun)}";
    };
    _captureFinalizeTimer.Tick += (_, __) =>
    {
      _captureFinalizeTimer.Stop();
      FinalizeCapture();
    };

    _ = LoadMacrosAsync();
    _ = LoadCompositesAsync();
  }

  private async Task LoadMacrosAsync()
  {
    var list = await _storageService.LoadAsync();
    Macros.Clear();
    foreach (var m in list) Macros.Add(m);
    if (Macros.Count > 0) SelectedMacro = Macros[0];
    else CreateNewMacro();
  }

  private async Task LoadCompositesAsync()
  {
    var list = await _compositeStorageService.LoadAsync();
    CompositeMacros.Clear();
    foreach (var c in list) CompositeMacros.Add(c);
    if (CompositeMacros.Count > 0) SelectedComposite = CompositeMacros[0];
  }

  // 합성 매크로는 기본 매크로 이름을 키로 참조하므로, 이름 중복 시 참조가 모호해진다.
  private string GenerateUniqueMacroName(string baseName)
  {
    if (Macros.All(m => m.Name != baseName)) return baseName;
    int n = 2;
    while (Macros.Any(m => m.Name == $"{baseName} ({n})")) n++;
    return $"{baseName} ({n})";
  }

  private void CreateNewMacro()
  {
    var macro = new Macro { Name = GenerateUniqueMacroName($"매크로 {Macros.Count + 1}") };
    Macros.Add(macro);
    SelectedMacro = macro;
    _ = SaveAsync();
  }

  private void CreateNewComposite()
  {
    var composite = new CompositeMacro { Name = $"합성 매크로 {CompositeMacros.Count + 1}" };
    CompositeMacros.Add(composite);
    SelectedComposite = composite;
    _ = SaveCompositesAsync();
  }

  private void StartRecording()
  {
    if (_selectedMacro == null || _state != AppState.Idle) return;
    _selectedMacro.Events.Clear();
    CurrentEvents.Clear();
    SetState(AppState.Recording);
    StatusText = "녹화 중... (F6 또는 Stop을 눌러 중단)";
    _recordingClock.Reset();
    _hookService.StartRecording();
    _keyboardHookService.StartRecording();
  }

  private void Stop(bool removeLastEvent = false)
  {
    if (_isCapturingStep)
    {
      CancelCaptureStep();
      return;
    }
    if (_state == AppState.Recording)
    {
      _hookService.StopRecording();
      _keyboardHookService.StopRecording();
      // 버튼 클릭으로 정지한 경우 마지막 이벤트(정지 버튼 클릭)를 제거
      if (removeLastEvent && _selectedMacro != null && _selectedMacro.Events.Count > 0)
      {
        _selectedMacro.Events.RemoveAt(_selectedMacro.Events.Count - 1);
        if (CurrentEvents.Count > 0)
          CurrentEvents.RemoveAt(CurrentEvents.Count - 1);
      }
      int count = CurrentEvents.Count;
      SetState(AppState.Idle);
      _ = SaveAfterRecordingAsync(count);
      return;
    }
    else if (_state == AppState.Playing)
    {
      _playCts?.Cancel();
      StatusText = "재생 중단됨";
    }
    SetState(AppState.Idle);
  }

  private async Task SaveAfterRecordingAsync(int eventCount)
  {
    await _storageService.SaveAsync(Macros);
    StatusText = $"녹화 완료 — {eventCount}개 이벤트 (자동 저장됨)";
  }

  private async Task StartPlayingAsync()
  {
    if (_selectedMacro == null || _selectedMacro.Events.Count == 0) return;
    if (_state != AppState.Idle) return;

    SetState(AppState.Playing);

    long runDurationMs = _selectedMacro.Events.Sum(e => e.DelayMs);

    void ReportProgress(int current)
    {
      string countLabel = _selectedMacro!.RepeatCount == 0 ? $"{current}/∞" : $"{current}/{_selectedMacro.RepeatCount}";
      StatusText = $"재생 중... ({countLabel})";
      _remainingMsInRun = runDurationMs;
      RemainingTimeText = $"남은 시간: {FormatRemaining(_remainingMsInRun)}";
    }
    ReportProgress(1);
    var progress = new Progress<int>(ReportProgress);

    _countdownTimer.Start();
    _playCts = new CancellationTokenSource();
    try
    {
      int completedRuns = await _playerService.PlayAsync(_selectedMacro, _playCts.Token, progress);
      StatusText = "재생 완료";
      MessageBox.Show($"{completedRuns}번 반복 완료했습니다.", "재생 완료",
        MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (OperationCanceledException)
    {
      StatusText = "재생 중단됨";
    }
    finally
    {
      _countdownTimer.Stop();
      RemainingTimeText = "";
      _playCts.Dispose();
      _playCts = null;
      SetState(AppState.Idle);
    }
  }

  private Macro? ResolveMacroByName(string name) => Macros.FirstOrDefault(m => m.Name == name);

  private async Task StartPlayingCompositeAsync()
  {
    if (_selectedComposite == null || _selectedComposite.Steps.Count == 0) return;
    if (_state != AppState.Idle) return;

    var missing = _selectedComposite.Steps
      .Where(s => s.StepType == CompositeStepType.MacroRef)
      .Select(s => s.MacroName)
      .Distinct()
      .Where(n => ResolveMacroByName(n) == null)
      .ToList();
    if (missing.Count > 0)
    {
      MessageBox.Show($"다음 기본 매크로를 찾을 수 없어 실행할 수 없습니다: {string.Join(", ", missing)}",
        "실행 불가", MessageBoxButton.OK, MessageBoxImage.Error);
      return;
    }

    SetState(AppState.Playing);

    long runDurationMs = CompositeMacroPlayerService.EstimateDurationMs(_selectedComposite, ResolveMacroByName);

    void ReportProgress(int current)
    {
      string countLabel = _selectedComposite!.RepeatCount == 0 ? $"{current}/∞" : $"{current}/{_selectedComposite.RepeatCount}";
      StatusText = $"합성 매크로 재생 중... ({countLabel})";
      _remainingMsInRun = runDurationMs;
      RemainingTimeText = $"남은 시간: {FormatRemaining(_remainingMsInRun)}";
    }
    ReportProgress(1);
    var progress = new Progress<int>(ReportProgress);

    _countdownTimer.Start();
    _playCts = new CancellationTokenSource();
    try
    {
      int completedRuns = await _compositePlayerService.PlayAsync(_selectedComposite, ResolveMacroByName, _playCts.Token, progress);
      StatusText = "재생 완료";
      MessageBox.Show($"{completedRuns}번 반복 완료했습니다.", "재생 완료",
        MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (OperationCanceledException)
    {
      StatusText = "재생 중단됨";
    }
    catch (MissingMacroException ex)
    {
      MessageBox.Show(ex.Message, "실행 불가", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    finally
    {
      _countdownTimer.Stop();
      RemainingTimeText = "";
      _playCts.Dispose();
      _playCts = null;
      SetState(AppState.Idle);
    }
  }

  private async Task SaveAsync()
  {
    await _storageService.SaveAsync(Macros);
    StatusText = "저장 완료";
  }

  private async Task SaveCompositesAsync()
  {
    await _compositeStorageService.SaveAsync(CompositeMacros);
    StatusText = "저장 완료";
  }

  public void SaveOnExit()
  {
    _storageService.SaveAsync(Macros).GetAwaiter().GetResult();
    _compositeStorageService.SaveAsync(CompositeMacros).GetAwaiter().GetResult();
  }

  public void SetSelectedMacros(IEnumerable<Macro> items)
  {
    if (_state != AppState.Idle) return;
    _selectedMacros.Clear();
    _selectedMacros.AddRange(items);
    RaiseAllCommands();
  }

  public void SetSelectedComposites(IEnumerable<CompositeMacro> items)
  {
    if (_state != AppState.Idle) return;
    _selectedComposites.Clear();
    _selectedComposites.AddRange(items);
    RaiseAllCommands();
  }

  private void DeleteSelected()
  {
    if (_selectedMacros.Count == 0) return;
    var toDelete = _selectedMacros.ToList();

    // 합성 매크로가 참조 중인 기본 매크로를 지우려는 경우 확인창을 띄운다
    var names = toDelete.Select(m => m.Name).ToHashSet();
    var usedBy = CompositeMacros
      .Where(c => c.Steps.Any(s => s.StepType == CompositeStepType.MacroRef && names.Contains(s.MacroName)))
      .Select(c => c.Name)
      .ToList();
    if (usedBy.Count > 0)
    {
      var msg = $"다음 합성 매크로에서 사용 중입니다: {string.Join(", ", usedBy)}\n정말 삭제하시겠습니까?";
      if (MessageBox.Show(msg, "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        return;
    }

    foreach (var m in toDelete)
      Macros.Remove(m);
    SelectedMacro = Macros.Count > 0 ? Macros[0] : null;
    RefreshAllStepKinds();
    _ = SaveAsync();
  }

  private void DeleteSelectedComposites()
  {
    if (_selectedComposites.Count == 0) return;
    var toDelete = _selectedComposites.ToList();
    foreach (var c in toDelete)
      CompositeMacros.Remove(c);
    SelectedComposite = CompositeMacros.Count > 0 ? CompositeMacros[0] : null;
    _ = SaveCompositesAsync();
  }

  // 선택된 매크로를 목록에서 한 칸 위(-1) 또는 아래(1)로 이동한다.
  private void MoveMacro(int offset)
  {
    if (_selectedMacro == null) return;
    int idx = Macros.IndexOf(_selectedMacro);
    int newIdx = idx + offset;
    if (idx < 0 || newIdx < 0 || newIdx >= Macros.Count) return;
    Macros.Move(idx, newIdx);
    RaiseAllCommands();
    _ = SaveAsync();
  }

  private void MoveComposite(int offset)
  {
    if (_selectedComposite == null) return;
    int idx = CompositeMacros.IndexOf(_selectedComposite);
    int newIdx = idx + offset;
    if (idx < 0 || newIdx < 0 || newIdx >= CompositeMacros.Count) return;
    CompositeMacros.Move(idx, newIdx);
    RaiseAllCommands();
    _ = SaveCompositesAsync();
  }

  private void DuplicateSelected()
  {
    if (_selectedMacros.Count == 0) return;
    Macro? last = null;
    foreach (var m in _selectedMacros.ToList())
    {
      var copy = new Macro
      {
        Name        = GenerateUniqueMacroName($"{m.Name} (복사본)"),
        RepeatCount = m.RepeatCount,
        Events      = m.Events.Select(e => new MacroEvent
        {
          EventType = e.EventType, Button = e.Button, ClickType = e.ClickType,
          X = e.X, Y = e.Y, KeyCode = e.KeyCode, DelayMs = e.DelayMs
        }).ToList()
      };
      Macros.Add(copy);
      last = copy;
    }
    if (last != null) SelectedMacro = last;
    _ = SaveAsync();
  }

  private void DuplicateSelectedComposites()
  {
    if (_selectedComposites.Count == 0) return;
    CompositeMacro? last = null;
    foreach (var c in _selectedComposites.ToList())
    {
      var copy = new CompositeMacro
      {
        Name        = $"{c.Name} (복사본)",
        RepeatCount = c.RepeatCount,
        Steps       = c.Steps.Select(s => new CompositeStep
        {
          StepType = s.StepType, DelayMs = s.DelayMs,
          MacroName = s.MacroName, RepeatCount = s.RepeatCount,
          EventType = s.EventType, Button = s.Button, ClickType = s.ClickType,
          X = s.X, Y = s.Y, KeyCode = s.KeyCode
        }).ToList()
      };
      CompositeMacros.Add(copy);
      last = copy;
    }
    if (last != null) SelectedComposite = last;
    _ = SaveCompositesAsync();
  }

  private void AddEventRow(MacroEvent evt, int number)
  {
    var row = new MacroEventRow(evt, number);
    row.PropertyChanged += (_, args) =>
    {
      if (args.PropertyName == nameof(MacroEventRow.Delay))
      {
        OnPropertyChanged(nameof(EstimatedDurationText));
        _ = SaveAsync();
      }
    };
    CurrentEvents.Add(row);
  }

  private void OnEventRecorded(MacroEvent evt)
  {
    Application.Current.Dispatcher.Invoke(() =>
    {
      if (_isCapturingStep) { HandleCaptureEvent(evt); return; }
      _selectedMacro?.Events.Add(evt);
      AddEventRow(evt, CurrentEvents.Count + 1);
      OnPropertyChanged(nameof(EstimatedDurationText));
    });
  }

  private void OnLastEventReplace(ClickType clickType, Models.MouseButton button)
  {
    Application.Current.Dispatcher.Invoke(() =>
    {
      if (_isCapturingStep)
      {
        if (_captureMouseStep != null)
        {
          _captureMouseStep.ClickType = clickType;
          RefreshStepList();
        }
        return;
      }
      if (_selectedMacro == null || _selectedMacro.Events.Count == 0) return;
      var last = _selectedMacro.Events[^1];
      last.ClickType = clickType;
      CurrentEvents.RemoveAt(CurrentEvents.Count - 1);
      AddEventRow(last, CurrentEvents.Count + 1);
    });
  }

  private void RefreshEventList()
  {
    CurrentEvents.Clear();
    if (_selectedMacro == null) return;
    for (int i = 0; i < _selectedMacro.Events.Count; i++)
      AddEventRow(_selectedMacro.Events[i], i + 1);
  }

  private void AddStepRow(CompositeStep step, int number)
  {
    var row = new CompositeStepRow(step, number, ResolveMacroByName);
    row.PropertyChanged += (_, args) =>
    {
      if (args.PropertyName == nameof(CompositeStepRow.Delay))
      {
        OnPropertyChanged(nameof(CompositeEstimatedDurationText));
        _ = SaveCompositesAsync();
      }
    };
    CurrentSteps.Add(row);
  }

  private void RefreshStepList()
  {
    CurrentSteps.Clear();
    if (_selectedComposite == null) return;
    for (int i = 0; i < _selectedComposite.Steps.Count; i++)
      AddStepRow(_selectedComposite.Steps[i], i + 1);
  }

  // 기본 매크로 목록이 바뀐 뒤(삭제/재생성) 현재 표시 중인 스텝들의 "(누락됨)" 표시를 갱신한다.
  private void RefreshAllStepKinds()
  {
    foreach (var row in CurrentSteps) row.Refresh(ResolveMacroByName);
  }

  private void AddStepToComposite(CompositeStep step)
  {
    if (_selectedComposite == null) return;
    _selectedComposite.Steps.Add(step);
    AddStepRow(step, CurrentSteps.Count + 1);
    OnPropertyChanged(nameof(CompositeEstimatedDurationText));
  }

  private void AddMacroRefStep()
  {
    if (_selectedComposite == null || MacroPickerPrompt == null) return;
    var (name, repeatCount) = MacroPickerPrompt(Macros.Select(m => m.Name));
    if (name == null) return;
    AddStepToComposite(new CompositeStep
    {
      StepType = CompositeStepType.MacroRef, MacroName = name, RepeatCount = Math.Max(1, repeatCount)
    });
    _ = SaveCompositesAsync();
  }

  private void RemoveSelectedStep()
  {
    if (_selectedComposite == null || _selectedStepRow == null) return;
    _selectedComposite.Steps.Remove(_selectedStepRow.Source);
    _selectedStepRow = null;
    RefreshStepList();
    _ = SaveCompositesAsync();
  }

  private void MoveStep(int offset)
  {
    if (_selectedComposite == null || _selectedStepRow == null) return;
    int idx = _selectedComposite.Steps.IndexOf(_selectedStepRow.Source);
    int newIdx = idx + offset;
    if (idx < 0 || newIdx < 0 || newIdx >= _selectedComposite.Steps.Count) return;
    var step = _selectedComposite.Steps[idx];
    _selectedComposite.Steps.RemoveAt(idx);
    _selectedComposite.Steps.Insert(newIdx, step);
    RefreshStepList();
    RaiseAllCommands();
    _ = SaveCompositesAsync();
  }

  // ---- 스텝 1개 원샷 캡처 ("입력 1개 캡처" 버튼) ----

  private void StartCaptureStep()
  {
    if (_selectedComposite == null || _state != AppState.Idle) return;
    _isCapturingStep = true;
    _capturePendingKeyDownVk = 0;
    _captureMouseStep = null;
    SetState(AppState.Recording);
    StatusText = "입력을 기다리는 중... (Esc로 취소)";
    _recordingClock.Reset();
    _hookService.StartRecording();
    _keyboardHookService.StartRecording();
  }

  private void CancelCaptureStep()
  {
    if (!_isCapturingStep) return;
    _captureFinalizeTimer.Stop();
    _hookService.StopRecording();
    _keyboardHookService.StopRecording();
    _isCapturingStep = false;
    _captureMouseStep = null;
    _capturePendingKeyDownVk = 0;
    StatusText = "캡처 취소됨";
    SetState(AppState.Idle);
  }

  private void HandleCaptureEvent(MacroEvent evt)
  {
    if (evt.EventType == MacroEventType.MouseClick)
    {
      _keyboardHookService.StopRecording();
      var step = new CompositeStep
      {
        StepType = CompositeStepType.RawEvent, EventType = MacroEventType.MouseClick,
        Button = evt.Button, ClickType = evt.ClickType, X = evt.X, Y = evt.Y
      };
      _captureMouseStep = step;
      AddStepToComposite(step);
      _captureFinalizeTimer.Interval = TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime());
      _captureFinalizeTimer.Start();
      return;
    }

    if (evt.EventType == MacroEventType.KeyDown && evt.KeyCode == NativeMethods.VK_ESCAPE)
    {
      CancelCaptureStep();
      return;
    }

    _hookService.StopRecording(); // 입력이 키보드로 확정되었으므로 마우스 훅은 더 필요 없다
    AddStepToComposite(new CompositeStep
    {
      StepType = CompositeStepType.RawEvent, EventType = evt.EventType, KeyCode = evt.KeyCode
    });

    if (evt.EventType == MacroEventType.KeyDown)
    {
      _capturePendingKeyDownVk = (uint)evt.KeyCode;
      return; // 대응하는 KeyUp을 기다린다
    }

    FinalizeCapture();
  }

  private void FinalizeCapture()
  {
    _hookService.StopRecording();
    _keyboardHookService.StopRecording();
    _isCapturingStep = false;
    _captureMouseStep = null;
    _capturePendingKeyDownVk = 0;
    StatusText = "입력 캡처 완료";
    SetState(AppState.Idle);
    _ = SaveCompositesAsync();
  }

  private void SetState(AppState state)
  {
    _state = state;
    RaiseAllCommands();
    OnPropertyChanged(nameof(MacroName));
    OnPropertyChanged(nameof(RepeatCountText));
    OnPropertyChanged(nameof(IsInfiniteRepeat));
    OnPropertyChanged(nameof(IsRepeatCountEnabled));
    OnPropertyChanged(nameof(CompositeName));
    OnPropertyChanged(nameof(CompositeRepeatCountText));
    OnPropertyChanged(nameof(CompositeIsInfiniteRepeat));
    OnPropertyChanged(nameof(CompositeIsRepeatCountEnabled));
  }

  private void RaiseAllCommands()
  {
    ((RelayCommand)RecordCommand).RaiseCanExecuteChanged();
    ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
    ((AsyncRelayCommand)PlayCommand).RaiseCanExecuteChanged();
    ((RelayCommand)NewMacroCommand).RaiseCanExecuteChanged();
    ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
    ((RelayCommand)DuplicateCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveMacroUpCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveMacroDownCommand).RaiseCanExecuteChanged();

    ((RelayCommand)NewCompositeCommand).RaiseCanExecuteChanged();
    ((RelayCommand)DeleteCompositeCommand).RaiseCanExecuteChanged();
    ((RelayCommand)DuplicateCompositeCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveCompositeUpCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveCompositeDownCommand).RaiseCanExecuteChanged();
    ((AsyncRelayCommand)PlayCompositeCommand).RaiseCanExecuteChanged();
    ((RelayCommand)AddMacroRefStepCommand).RaiseCanExecuteChanged();
    ((RelayCommand)CaptureStepCommand).RaiseCanExecuteChanged();
    ((RelayCommand)RemoveStepCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveStepUpCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveStepDownCommand).RaiseCanExecuteChanged();
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
