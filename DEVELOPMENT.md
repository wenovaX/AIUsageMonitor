# AIUsageMonitor Developer Notes

This document contains the implementation details that do not belong in the user-facing README.

[한국어 개발자 문서](DEVELOPMENT.kr.md) · [User README](README.md)

## Scope

**AIUsageMonitor monitors quota. It does not automate provider-side account switching.**

The app keeps credentials required for monitoring, calls provider endpoints, and renders the returned usage state.<br>
Actual product usage and account switching remain in Google, ChatGPT/Codex, and Cursor.

## Stack

- .NET 10
- .NET MAUI, primarily targeting Windows
- WinUI 3 and WebView2 on Windows
- `H.NotifyIcon` for tray integration
- `Microsoft.Data.Sqlite` for reading Cursor's local state database

## Core Areas

- `MainPage.xaml` / `MainPage.xaml.cs`: dashboard UI, refresh queues, and account workflows
- `Services/*ApiService.cs`: provider requests and response parsing
- `Services/*AccountManagerService.cs`: account collections and persistence
- `Services/TokenStorageService.cs`: encrypted monitoring-token persistence
- `Services/AppDataPaths.cs`: executable-local paths and legacy migration
- `PlatformImplementations/WindowsController.cs`: window, tray, and shutdown behavior

## Local Data Boundary

Runtime data is rooted beside the executable:

```text
userdata/
  data/
    accounts.json
    codex_accounts.json
    cursor_accounts.json
    tokens.json
    notification_settings.json
    discovered_models.json
  webview/
```

Important behavior:

- Tokens are excluded from account JSON models and stored encrypted in `tokens.json`.
- Cursor sensitive fields are encrypted before persistence.
- WebView2 cookies and cache use `userdata/webview`.
- Known legacy AppData, SecureStorage, and single-file extraction data is migrated when discovered.
- `userdata/`, `build/`, `bin/`, and `obj/` must remain ignored by Git.
- `userdata/`, `build/`, `.git/`, and `.vs/` must also remain excluded from publish inputs.

Local encryption reduces accidental disclosure. It is not a defense against malware already running as the same OS user.

The executable-local data layout is a deliberate risk-management choice.<br>
System profile paths such as AppData are convenient but predictable.<br>
After a machine compromise, placing session-adjacent app data under a visible `userdata` directory beside the executable makes it easier for the user to inspect, move, back up, delete, or isolate the entire trust boundary.

## Refresh Model

- Account refreshes use bounded global and provider-specific queues.
- Manual refresh receives higher priority than background refresh.
- Background loops and pending UI callbacks are cancelled during shutdown.
- Account removal and token persistence must remain asynchronous to avoid UI-thread deadlocks.

## Codex Diagnostics

The Codex usage endpoint changes occasionally. Raw usage JSON can be printed in Debug builds with:

```text
AIUSAGEMONITOR_CODEX_RAW_LOG=1
```

The Visual Studio `Windows Machine` profile enables this variable. Output is written to the debugger through `Debug.WriteLine`;<br>
Release binaries do not emit it.<br>
Authorization headers and access tokens are not logged.

Do not infer quota policy from labels alone. For example, a Free response may currently report a 30-day `limit_window_seconds`;<br>
do not hardcode a weekly reset unless the API provides evidence or repeated observations establish the rule.

## Build

Standard verification:

```powershell
dotnet restore
dotnet build AIUsageMonitor.csproj -f net10.0-windows10.0.19041.0 -c Release
```

Single-executable build:

```powershell
.\build-as-binary.bat
```

The script creates:

```text
build/Release_yyyyMMdd_HHmmss/
  AIUsageMonitor.exe
  AIUsageMonitor-v{version}-win-x64-single-file-yyyyMMdd_HHmmss.zip
```

The binary is self-contained, compressed, and configured with `UseMonoRuntime=false` for the Windows CoreCLR runtime.<br>
The release zip contains only `AIUsageMonitor.exe`.

Do not allow build artifacts to become publish inputs.<br>
If `build/` is included by default item discovery, previous release zips can be bundled into the next single-file executable and cause runaway binary growth.

## Before Committing

- Build the Windows Release target.
- Confirm `userdata`, raw logs, tokens, `.vs`, `bin`, `obj`, and `build` are not staged.
- Review provider response-model changes separately from UI changes.
- Keep raw diagnostic logging opt-in.
