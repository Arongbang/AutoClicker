# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 빌드 및 실행

```bash
# 개발 빌드 및 실행
dotnet run

# Release 빌드
dotnet build -c Release

# 단일 exe로 배포 (self-contained, x64)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# 결과물: publish\AutoClicker.exe
```

> 코드 수정 후에는 반드시 `dotnet publish`까지 실행하여 `publish\AutoClicker.exe`를 생성한다.

## 아키텍처

MVVM 패턴 기반 WPF 애플리케이션이다.

```
View (MainWindow.xaml)
  ↕ DataBinding
ViewModel (MainViewModel)
  ↕ 의존
Services (MouseHookService, MacroPlayerService, MacroStorageService)
  ↕
Models (Macro, MacroEvent)
```

### 상태 머신

`MainViewModel`은 `AppState` enum으로 상태를 관리한다.

- `Idle` → `Recording` (F6 또는 녹화 버튼)
- `Idle` → `Playing` (F7 또는 재생 버튼)
- `Recording` / `Playing` → `Idle` (F6/F7 또는 정지 버튼)

모든 커맨드의 `CanExecute`는 이 상태에 의존하므로, 상태 변경 시 반드시 `CommandManager.InvalidateRequerySuggested()`를 호출해야 한다.

### 핵심 서비스

**MouseHookService** — `SetWindowsHookEx(WH_MOUSE_LL)`로 저수준 마우스 후킹.  
더블클릭은 OS 더블클릭 타임 내 같은 좌표 클릭을 감지해 마지막 이벤트를 Single→Double로 교체한다 (`LastEventReplace` 이벤트).

**MacroPlayerService** — `SendInput` API로 마우스 조작. 좌표는 물리 픽셀에서 정규화 좌표(0~65535)로 변환하며, 다중 모니터를 위해 가상 데스크탑 전체 크기(`SM_CXVIRTUALSCREEN`)를 기준으로 한다.

**MacroStorageService** — `%AppData%\AutoClicker\macros.json`에 JSON으로 저장. Enum은 문자열로 직렬화된다.

### 핫키

`MainWindow.xaml.cs`에서 `RegisterHotKey` / `WndProc`으로 처리한다.

- **F6**: 녹화 시작/중지 토글
- **F7**: 재생 시작/중지 토글

### 스레드 고려사항

마우스 후킹 콜백은 별도 스레드에서 실행되므로, UI 업데이트 시 `Dispatcher.Invoke()`를 사용해야 한다.
