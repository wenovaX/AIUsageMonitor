# AIUsageMonitor

> Premium AI usage monitoring dashboard for Antigravity and Codex.

![AIUsageMonitor Header](Resources/Images/app_title.png)

## Overview

AIUsageMonitor is a .NET MAUI desktop app for tracking usage, limits, and quota windows across multiple AI accounts in one place. It is currently tuned for Windows desktop usage.

## Screenshots

| Antigravity (Google) | Codex (OpenAI/GitHub) |
| :---: | :---: |
| ![Antigravity Preview](Preview/Preview-Antigravity.png) | ![Codex Preview](Preview/Preview-Codex.png) |

## Download

Download the latest build from the Releases page.

## Key Features

### Multi-service support
- Antigravity account and model usage tracking
- Codex session and weekly quota monitoring
- Unified dashboard for mixed account setups

### Windows tray workflow
- Tray icon with left click and double click restore
- Close-to-tray behavior with exit confirmation
- Optional remember-choice flow when closing the window
- Tray notification when the app is sent to the background through the close dialog

### Refresh and monitoring
- Manual full refresh from the header
- `F5` keyboard shortcut for full refresh on the current tab
- Background refresh queue with limited concurrency for better responsiveness
- Retry-aware refresh behavior for network-driven account updates

### Privacy and usability
- Anonymous mode for screen sharing
- Antigravity model list controls with default reset and manual update flow
- Tabbed UI for Google, Codex, Settings, and About

## Requirements

- .NET 10.0 SDK
- Visual Studio 2022 with .NET MAUI workload
- Windows 10/11

## Build from Source

1. Clone the repository.
2. Open `AIUsageMonitor.sln` in Visual Studio 2022.
3. Restore NuGet packages.
4. Run the `Windows Machine` target.

## Authentication

### Antigravity (Google)
1. Open the Antigravity tab.
2. Click `+ Add Account`.
3. Complete the Google OAuth flow in the browser.

### Codex (OpenAI / GitHub)
1. Open the Codex tab.
2. Click `+ Add Account`.
3. Choose OpenAI login, GitHub login, or manual token entry.

## Antigravity model list

- The app starts with a default model set and canonical order:
  - `Gemini 3.1 Pro (High)`
  - `Gemini 3.1 Pro (Low)`
  - `Gemini 3 Flash`
  - `Claude Sonnet 4.6 (Thinking)`
  - `Claude Opus 4.6 (Thinking)`
  - `GPT-OSS 120B (Medium)`
- `Update Model List` scans already-fetched Antigravity quota data and appends newly discovered models.
- Newly discovered models are added as disabled by default so you can opt in manually.
- `Set to Default` restores the default list above.
- Missing discovered models remain visible in Settings as `Missing` instead of being silently removed.

## Notes

- Version: `v1.0.4`
- Windows tray behavior is handled through the platform controller layer.
- The tray icon is shipped as `trayicon.ico` for more reliable Windows system tray rendering.

## Privacy

- Tokens and settings are stored locally.
- The app talks directly to provider endpoints.
- Review the source before using it with sensitive accounts.

## License

Distributed under the MIT License. See `LICENSE` for details.
