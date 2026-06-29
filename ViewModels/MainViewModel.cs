using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoClicker.Models;
using AutoClicker.Services;

namespace AutoClicker.ViewModels;

public class MacroEventRow
{
  public int    Number { get; }
  public string Kind   { get; }
  public int    X      { get; }
  public int    Y      { get; }
  public string Delay  { get; }

  public MacroEventRow(MacroEvent e, int number)
  {
    Number = number;
    Kind   = $"{e.Button} {(e.ClickType == ClickType.Double ? "더블클릭" : "클릭")}";
    X      = e.X;
    Y      = e.Y;
    Delay  = e.DelayMs >= 1000
      ? $"{e.DelayMs / 1000.0:F1}s"
      : $"{e.DelayMs}ms";
  }
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
    }
  }

  public string RepeatCountText
  {
    get => _selectedMacro?.RepeatCount.ToString() ?? "1";
    set
    {
      if (_selectedMacro == null) return;
      if (int.TryParse(value, out int n) && n >= 1)
        _selectedMacro.RepeatCount = n;
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
    }
  }

  public bool IsRepeatCountEnabled => !IsInfiniteRepeat;

  private string _statusText = "대기 중";
  public string StatusText
  {
    get => _statusText;
    private set { _statusText = value; OnPropertyChanged(); }
  }

  // 커맨드
  public ICommand RecordCommand  { get; }
  public ICommand StopCommand    { get; }
  public ICommand PlayCommand    { get; }
  public ICommand NewMacroCommand  { get; }
  public ICommand DeleteCommand  { get; }
  public ICommand ToggleRecordCommand { get; }
  public ICommand TogglePlayCommand   { get; }

  public MainViewModel()
  {
    RecordCommand   = new RelayCommand(_ => StartRecording(),  _ => _state == AppState.Idle && _selectedMacro != null);
    StopCommand     = new RelayCommand(_ => Stop(removeLastEvent: true), _ => _state != AppState.Idle);
    PlayCommand     = new AsyncRelayCommand(_ => StartPlayingAsync(), _ => _state == AppState.Idle && _selectedMacro?.Events.Count > 0);
    NewMacroCommand = new RelayCommand(_ => CreateNewMacro(), _ => _state == AppState.Idle);
    DeleteCommand   = new RelayCommand(_ => DeleteSelected(),   _ => _state == AppState.Idle && _selectedMacro != null);

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

    _ = LoadMacrosAsync();
  }

  private async Task LoadMacrosAsync()
  {
    var list = await _storageService.LoadAsync();
    Macros.Clear();
    foreach (var m in list) Macros.Add(m);
    if (Macros.Count > 0) SelectedMacro = Macros[0];
  }

  private void CreateNewMacro()
  {
    var macro = new Macro { Name = $"매크로 {Macros.Count + 1}" };
    Macros.Add(macro);
    SelectedMacro = macro;
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
    string repeatLabel = _selectedMacro.RepeatCount == 0
      ? "무한 반복"
      : $"{_selectedMacro.RepeatCount}회";
    StatusText = $"재생 중... ({repeatLabel})";

    _playCts = new CancellationTokenSource();
    try
    {
      int completedRuns = await _playerService.PlayAsync(_selectedMacro, _playCts.Token);
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

  private void DeleteSelected()
  {
    if (_selectedMacro == null) return;
    Macros.Remove(_selectedMacro);
    SelectedMacro = Macros.Count > 0 ? Macros[0] : null;
    _ = SaveAsync();
  }

  private void OnEventRecorded(MacroEvent evt)
  {
    Application.Current.Dispatcher.Invoke(() =>
    {
      _selectedMacro?.Events.Add(evt);
      CurrentEvents.Add(new MacroEventRow(evt, CurrentEvents.Count + 1));
    });
  }

  private void OnLastEventReplace(ClickType clickType, Models.MouseButton button)
  {
    Application.Current.Dispatcher.Invoke(() =>
    {
      if (_selectedMacro == null || _selectedMacro.Events.Count == 0) return;
      var last = _selectedMacro.Events[^1];
      last.ClickType = clickType;
      var replaced = new MacroEventRow(last, CurrentEvents.Count);
      CurrentEvents.RemoveAt(CurrentEvents.Count - 1);
      CurrentEvents.Add(replaced);
    });
  }

  private void RefreshEventList()
  {
    CurrentEvents.Clear();
    if (_selectedMacro == null) return;
    for (int i = 0; i < _selectedMacro.Events.Count; i++)
      CurrentEvents.Add(new MacroEventRow(_selectedMacro.Events[i], i + 1));
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
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
