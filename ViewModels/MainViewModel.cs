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

  private static readonly Regex SingleDelayPattern   = new(@"^([0-9]*\.?[0-9]+)\s*(ms|s|m|h)?$", RegexOptions.IgnoreCase);
  private static readonly Regex CombinedDelayPattern = new(
    @"^(?:([0-9]*\.?[0-9]+)h)?\s*(?:([0-9]*\.?[0-9]+)m)?\s*(?:([0-9]*\.?[0-9]+)s)?\s*(?:([0-9]*\.?[0-9]+)ms)?$",
    RegexOptions.IgnoreCase);

  // "500", "500ms", "1.5s", "2m", "1h" 등 단일 단위 입력과
  // "5m20s", "1h12m30s" 처럼 h/m/s/ms를 이어 쓴 복합 단위 입력을 모두 밀리초로 변환한다.
  public static bool TryParseDelay(string? text, out long ms)
  {
    ms = 0;
    if (string.IsNullOrWhiteSpace(text)) return false;
    var trimmed = text.Trim();

    var single = SingleDelayPattern.Match(trimmed);
    if (single.Success)
    {
      if (!double.TryParse(single.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        return false;
      var unitMs = single.Groups[2].Value.ToLowerInvariant() switch
      {
        "h" => 3_600_000d,
        "m" => 60_000d,
        "s" => 1_000d,
        _   => 1d,
      };
      ms = (long)Math.Round(value * unitMs);
      return true;
    }

    var combined = CombinedDelayPattern.Match(trimmed);
    bool hasAnyUnit = combined.Groups[1].Success || combined.Groups[2].Success
                    || combined.Groups[3].Success || combined.Groups[4].Success;
    if (!combined.Success || !hasAnyUnit) return false;

    double totalMs = 0;
    if (combined.Groups[1].Success) totalMs += ParseUnit(combined.Groups[1].Value) * 3_600_000d;
    if (combined.Groups[2].Success) totalMs += ParseUnit(combined.Groups[2].Value) * 60_000d;
    if (combined.Groups[3].Success) totalMs += ParseUnit(combined.Groups[3].Value) * 1_000d;
    if (combined.Groups[4].Success) totalMs += ParseUnit(combined.Groups[4].Value);
    ms = (long)Math.Round(totalMs);
    return true;
  }

  private static double ParseUnit(string value) =>
    double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

  public static string KeyName(int vk) => KeyInterop.KeyFromVirtualKey(vk).ToString();
}

public class MacroEventRow : INotifyPropertyChanged
{
  internal readonly MacroEvent Source;

  public int    Number { get; }
  public string Kind   { get; private set; } = "";
  public string X      { get; private set; } = "";
  public string Y      { get; private set; } = "";

  public string Delay
  {
    // Empty(빈 이벤트)는 지연도 붙여넣기 전까지 빈 값으로 표시한다.
    get => Source.EventType == MacroEventType.Empty && Source.DelayMs == 0
      ? "" : EventDisplay.FormatDelay(Source.DelayMs);
    set
    {
      if (!EventDisplay.TryParseDelay(value, out var ms) || ms < 0) return;
      Source.DelayMs = ms;
      OnPropertyChanged();
    }
  }

  public string Description
  {
    get => Source.Description;
    set
    {
      if (value == Source.Description) return;
      Source.Description = value;
      OnPropertyChanged();
    }
  }

  public MacroEventRow(MacroEvent e, int number)
  {
    Source = e;
    Number = number;
    RefreshFields();
  }

  // 붙여넣기로 Source 내용이 바뀐 뒤 표시값을 다시 계산한다.
  public void Refresh()
  {
    RefreshFields();
    OnPropertyChanged(nameof(Kind));
    OnPropertyChanged(nameof(X));
    OnPropertyChanged(nameof(Y));
    OnPropertyChanged(nameof(Delay));
    OnPropertyChanged(nameof(Description));
  }

  private void RefreshFields()
  {
    Kind = Source.EventType switch
    {
      MacroEventType.MouseClick => $"{Source.Button} {(Source.ClickType == ClickType.Double ? "더블클릭" : "클릭")}",
      MacroEventType.KeyDown    => $"{EventDisplay.KeyName(Source.KeyCode)} 누름",
      MacroEventType.KeyUp      => $"{EventDisplay.KeyName(Source.KeyCode)} 뗌",
      MacroEventType.Empty      => "",
      _                         => "?",
    };
    X = Source.EventType == MacroEventType.Empty ? "" : Source.X.ToString();
    Y = Source.EventType == MacroEventType.Empty ? "" : Source.Y.ToString();
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
    // Empty(빈 스텝)는 지연도 붙여넣기 전까지 빈 값으로 표시한다.
    get => Source.StepType == CompositeStepType.RawEvent && Source.EventType == MacroEventType.Empty && Source.DelayMs == 0
      ? "" : EventDisplay.FormatDelay(Source.DelayMs);
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

  // 매크로 목록이 바뀌었거나(삭제/재생성) 붙여넣기로 내용이 바뀐 뒤 표시값을 다시 계산한다.
  public void Refresh(Func<string, Macro?> resolveMacro)
  {
    Kind = BuildKind(Source, resolveMacro);
    OnPropertyChanged(nameof(Kind));
    OnPropertyChanged(nameof(Delay));
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
      MacroEventType.Empty      => "",
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

  // '기록된 이벤트' 목록의 복사/붙여넣기용 클립보드(단일 슬롯). 합성 매크로의 구성 스텝 붙여넣기도 이 클립보드를 공유한다.
  private MacroEvent? _copiedMacroEvent;

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

  private MacroEventRow? _selectedEventRow;
  public MacroEventRow? SelectedEventRow
  {
    get => _selectedEventRow;
    set { _selectedEventRow = value; OnPropertyChanged(); RaiseAllCommands(); }
  }

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
  public ICommand AddEventCommand    { get; }
  public ICommand CopyEventCommand   { get; }
  public ICommand PasteEventCommand  { get; }
  public ICommand DeleteEventCommand { get; }
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
  public ICommand AddEventStepCommand      { get; }
  public ICommand PasteStepCommand         { get; }
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
    AddEventCommand    = new RelayCommand(_ => InsertBlankEvent(),     _ => _state == AppState.Idle && _selectedMacro != null);
    CopyEventCommand   = new RelayCommand(_ => CopySelectedEvent(),    _ => _state == AppState.Idle && _selectedEventRow != null);
    PasteEventCommand  = new RelayCommand(_ => PasteIntoSelectedEvent(), _ => _state == AppState.Idle && _selectedEventRow != null && _copiedMacroEvent != null);
    DeleteEventCommand = new RelayCommand(_ => DeleteSelectedEvent(),  _ => _state == AppState.Idle && _selectedEventRow != null);
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
    AddEventStepCommand       = new RelayCommand(_ => InsertBlankStep(),      _ => _state == AppState.Idle && _selectedComposite != null);
    PasteStepCommand         = new RelayCommand(_ => PasteIntoSelectedStep(), _ => _state == AppState.Idle && _selectedStepRow != null && _copiedMacroEvent != null);
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

    if (_selectedMacro.Events.Any(e => e.EventType == MacroEventType.Empty))
    {
      MessageBox.Show("빈 이벤트가 있습니다. 삭제하거나 복사·붙여넣기로 채운 뒤 다시 시도하세요.",
        "재생 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

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

    var emptyEventMacros = _selectedComposite.Steps
      .Where(s => s.StepType == CompositeStepType.MacroRef)
      .Select(s => s.MacroName)
      .Distinct()
      .Where(n => ResolveMacroByName(n)?.Events.Any(e => e.EventType == MacroEventType.Empty) == true)
      .ToList();
    if (emptyEventMacros.Count > 0)
    {
      MessageBox.Show($"다음 기본 매크로에 빈 이벤트가 있어 실행할 수 없습니다: {string.Join(", ", emptyEventMacros)}",
        "실행 불가", MessageBoxButton.OK, MessageBoxImage.Error);
      return;
    }

    if (_selectedComposite.Steps.Any(s => s.StepType == CompositeStepType.RawEvent && s.EventType == MacroEventType.Empty))
    {
      MessageBox.Show("빈 스텝이 있습니다. 삭제하거나 복사·붙여넣기로 채운 뒤 다시 시도하세요.",
        "실행 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
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
          X = e.X, Y = e.Y, KeyCode = e.KeyCode, DelayMs = e.DelayMs, Description = e.Description
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
      else if (args.PropertyName == nameof(MacroEventRow.Description))
      {
        _ = SaveAsync();
      }
    };
    CurrentEvents.Add(row);
  }

  private void OnEventRecorded(MacroEvent evt)
  {
    Application.Current.Dispatcher.Invoke(() =>
    {
      _selectedMacro?.Events.Add(evt);
      AddEventRow(evt, CurrentEvents.Count + 1);
      OnPropertyChanged(nameof(EstimatedDurationText));
    });
  }

  private void OnLastEventReplace(ClickType clickType, Models.MouseButton button)
  {
    Application.Current.Dispatcher.Invoke(() =>
    {
      if (_selectedMacro == null || _selectedMacro.Events.Count == 0) return;
      var last = _selectedMacro.Events[^1];
      last.ClickType = clickType;
      CurrentEvents.RemoveAt(CurrentEvents.Count - 1);
      AddEventRow(last, CurrentEvents.Count + 1);
    });
  }

  // '추가' 버튼: 선택된 행 바로 다음(선택 없으면 맨 끝)에 값이 빈 이벤트를 삽입한다.
  // 종류/X/Y는 읽기 전용 컬럼이라 직접 입력할 수 없으므로, 기존 행을 복사해 붙여넣어 채우는 용도다.
  private void InsertBlankEvent()
  {
    if (_selectedMacro == null) return;
    int insertIdx = _selectedEventRow != null
      ? _selectedMacro.Events.IndexOf(_selectedEventRow.Source) + 1
      : _selectedMacro.Events.Count;
    _selectedMacro.Events.Insert(insertIdx, new MacroEvent { EventType = MacroEventType.Empty });
    RefreshEventList();
    SelectedEventRow = CurrentEvents[insertIdx];
    OnPropertyChanged(nameof(EstimatedDurationText));
    _ = SaveAsync();
  }

  private void CopySelectedEvent()
  {
    if (_selectedEventRow == null) return;
    var s = _selectedEventRow.Source;
    _copiedMacroEvent = new MacroEvent
    {
      EventType = s.EventType, Button = s.Button, ClickType = s.ClickType,
      X = s.X, Y = s.Y, KeyCode = s.KeyCode, DelayMs = s.DelayMs, Description = s.Description
    };
  }

  private void PasteIntoSelectedEvent()
  {
    if (_selectedEventRow == null || _copiedMacroEvent == null) return;
    var target = _selectedEventRow.Source;
    var c = _copiedMacroEvent;
    target.EventType   = c.EventType;
    target.Button      = c.Button;
    target.ClickType   = c.ClickType;
    target.X           = c.X;
    target.Y           = c.Y;
    target.KeyCode     = c.KeyCode;
    target.DelayMs     = c.DelayMs;
    target.Description = c.Description;
    _selectedEventRow.Refresh(); // Kind/X/Y/Delay 표시 갱신 → Delay 변경 통지로 저장까지 이어진다
  }

  private void DeleteSelectedEvent()
  {
    if (_selectedMacro == null || _selectedEventRow == null) return;
    _selectedMacro.Events.Remove(_selectedEventRow.Source);
    _selectedEventRow = null;
    RefreshEventList();
    OnPropertyChanged(nameof(EstimatedDurationText));
    _ = SaveAsync();
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

  // ---- 스텝 목록의 빈 입력 추가/붙여넣기 ("+ 빈 입력 추가" / "붙여넣기" 버튼) ----

  // '입력 추가' 버튼: 선택된 스텝 바로 다음(선택 없으면 맨 끝)에 값이 빈 RawEvent 스텝을 삽입한다.
  // 종류는 읽기 전용 칼럼이라 직접 입력할 수 없으므로, 기본 매크로의 이벤트를 복사해 붙여넣는 용도다.
  private void InsertBlankStep()
  {
    if (_selectedComposite == null) return;
    int insertIdx = _selectedStepRow != null
      ? _selectedComposite.Steps.IndexOf(_selectedStepRow.Source) + 1
      : _selectedComposite.Steps.Count;
    _selectedComposite.Steps.Insert(insertIdx,
      new CompositeStep { StepType = CompositeStepType.RawEvent, EventType = MacroEventType.Empty });
    RefreshStepList();
    SelectedStepRow = CurrentSteps[insertIdx];
    OnPropertyChanged(nameof(CompositeEstimatedDurationText));
    _ = SaveCompositesAsync();
  }

  // 기본 매크로의 '기록된 이벤트' 목록에서 복사한 내용을 선택된 스텝에 덮어쓴다.
  private void PasteIntoSelectedStep()
  {
    if (_selectedStepRow == null || _copiedMacroEvent == null) return;
    var target = _selectedStepRow.Source;
    var c = _copiedMacroEvent;
    target.StepType  = CompositeStepType.RawEvent;
    target.EventType = c.EventType;
    target.Button    = c.Button;
    target.ClickType = c.ClickType;
    target.X         = c.X;
    target.Y         = c.Y;
    target.KeyCode   = c.KeyCode;
    target.DelayMs   = c.DelayMs;
    _selectedStepRow.Refresh(ResolveMacroByName);
    OnPropertyChanged(nameof(CompositeEstimatedDurationText));
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
    ((RelayCommand)AddEventCommand).RaiseCanExecuteChanged();
    ((RelayCommand)CopyEventCommand).RaiseCanExecuteChanged();
    ((RelayCommand)PasteEventCommand).RaiseCanExecuteChanged();
    ((RelayCommand)DeleteEventCommand).RaiseCanExecuteChanged();
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
    ((RelayCommand)AddEventStepCommand).RaiseCanExecuteChanged();
    ((RelayCommand)PasteStepCommand).RaiseCanExecuteChanged();
    ((RelayCommand)RemoveStepCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveStepUpCommand).RaiseCanExecuteChanged();
    ((RelayCommand)MoveStepDownCommand).RaiseCanExecuteChanged();
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
