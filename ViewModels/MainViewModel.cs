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

public class MacroEventRow : INotifyPropertyChanged
{
  private readonly MacroEvent _source;

  public int    Number { get; }
  public string Kind   { get; }
  public int    X      { get; }
  public int    Y      { get; }

  public string Delay
  {
    get => FormatDelay(_source.DelayMs);
    set
    {
      if (!TryParseDelay(value, out var ms) || ms < 0) return;
      _source.DelayMs = ms;
      OnPropertyChanged();
    }
  }

  // 지연시간을 가장 자연스러운 단위(h/m/s/ms)로 표시한다.
  private static string FormatDelay(long ms)
  {
    if (ms != 0 && ms % 3_600_000 == 0) return $"{ms / 3_600_000}h";
    if (ms != 0 && ms % 60_000    == 0) return $"{ms / 60_000}m";
    if (ms != 0 && ms % 1_000     == 0) return $"{ms / 1_000}s";
    return $"{ms}ms";
  }

  // "500", "500ms", "1.5s", "2m", "1h" 등 ms/s/m/h 단위 입력을 밀리초로 변환한다.
  private static bool TryParseDelay(string? text, out long ms)
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

  public MacroEventRow(MacroEvent e, int number)
  {
    _source = e;
    Number  = number;
    Kind    = $"{e.Button} {(e.ClickType == ClickType.Double ? "더블클릭" : "클릭")}";
    X       = e.X;
    Y       = e.Y;
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? n = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public enum AppState { Idle, Recording, Playing }

public class MainViewModel : INotifyPropertyChanged
{
  private readonly MouseHookService   _hookService    = new();
  private readonly MacroPlayerService _playerService  = new();
  private readonly MacroStorageService _storageService = new();

  private AppState _state = AppState.Idle;
  private CancellationTokenSource? _playCts;

  public ObservableCollection<Macro>        Macros        { get; } = new();
  public ObservableCollection<MacroEventRow> CurrentEvents { get; } = new();

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

  public MainViewModel()
  {
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
      else if (_state == AppState.Idle && _selectedMacro != null) StartRecording();
    });
    TogglePlayCommand = new RelayCommand(_ =>
    {
      if (_state == AppState.Playing) Stop();
      else if (_state == AppState.Idle && _selectedMacro?.Events.Count > 0)
        _ = StartPlayingAsync();
    });

    _hookService.EventRecorded   += OnEventRecorded;
    _hookService.LastEventReplace += OnLastEventReplace;
    _countdownTimer.Tick += (_, __) =>
    {
      _remainingMsInRun = Math.Max(0, _remainingMsInRun - 1000);
      RemainingTimeText = $"남은 시간: {FormatRemaining(_remainingMsInRun)}";
    };

    _ = LoadMacrosAsync();
  }

  private async Task LoadMacrosAsync()
  {
    var list = await _storageService.LoadAsync();
    Macros.Clear();
    foreach (var m in list) Macros.Add(m);
    if (Macros.Count > 0) SelectedMacro = Macros[0];
    else CreateNewMacro();
  }

  private void CreateNewMacro()
  {
    var macro = new Macro { Name = $"매크로 {Macros.Count + 1}" };
    Macros.Add(macro);
    SelectedMacro = macro;
    _ = SaveAsync();
  }

  private void StartRecording()
  {
    if (_selectedMacro == null || _state != AppState.Idle) return;
    _selectedMacro.Events.Clear();
    CurrentEvents.Clear();
    SetState(AppState.Recording);
    StatusText = "녹화 중... (F6 또는 Stop을 눌러 중단)";
    _hookService.StartRecording();
  }

  private void Stop(bool removeLastEvent = false)
  {
    if (_state == AppState.Recording)
    {
      _hookService.StopRecording();
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

  private async Task SaveAsync()
  {
    await _storageService.SaveAsync(Macros);
    StatusText = "저장 완료";
  }

  public void SaveOnExit() =>
    _storageService.SaveAsync(Macros).GetAwaiter().GetResult();

  public void SetSelectedMacros(IEnumerable<Macro> items)
  {
    if (_state != AppState.Idle) return;
    _selectedMacros.Clear();
    _selectedMacros.AddRange(items);
    RaiseAllCommands();
  }

  private void DeleteSelected()
  {
    if (_selectedMacros.Count == 0) return;
    var toDelete = _selectedMacros.ToList();
    foreach (var m in toDelete)
      Macros.Remove(m);
    SelectedMacro = Macros.Count > 0 ? Macros[0] : null;
    _ = SaveAsync();
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

  private void DuplicateSelected()
  {
    if (_selectedMacros.Count == 0) return;
    Macro? last = null;
    foreach (var m in _selectedMacros.ToList())
    {
      var copy = new Macro
      {
        Name        = $"{m.Name} (복사본)",
        RepeatCount = m.RepeatCount,
        Events      = m.Events.Select(e => new MacroEvent
        {
          Button = e.Button, ClickType = e.ClickType, X = e.X, Y = e.Y, DelayMs = e.DelayMs
        }).ToList()
      };
      Macros.Add(copy);
      last = copy;
    }
    if (last != null) SelectedMacro = last;
    _ = SaveAsync();
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

  private void RefreshEventList()
  {
    CurrentEvents.Clear();
    if (_selectedMacro == null) return;
    for (int i = 0; i < _selectedMacro.Events.Count; i++)
      AddEventRow(_selectedMacro.Events[i], i + 1);
  }

  private void SetState(AppState state)
  {
    _state = state;
    RaiseAllCommands();
    OnPropertyChanged(nameof(MacroName));
    OnPropertyChanged(nameof(RepeatCountText));
    OnPropertyChanged(nameof(IsInfiniteRepeat));
    OnPropertyChanged(nameof(IsRepeatCountEnabled));
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
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
