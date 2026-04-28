# AIUsageMonitor

> Korean README: [README.kr.md](README.kr.md)

> Premium AI usage monitoring dashboard for Antigravity, Codex, and Cursor.

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
- Cursor Composer context usage monitoring from the local Cursor database
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
- Cursor account rename support for local/session-based accounts
- Tabbed UI for Antigravity, Codex, Cursor, Settings, and About

## Requirements

- .NET 10.0 SDK
- Visual Studio 2026 with .NET MAUI workload
- Windows 10/11

## Build from Source

1. Clone the repository.
2. Open `AIUsageMonitor.sln` in Visual Studio.
3. Restore NuGet packages.
4. Run the `Windows Machine` target.

## Authentication

### Antigravity (Google)
1. Open the **Antigravity** tab.
2. Click **+ Add Account**.
3. Complete the Google OAuth flow in the browser.
4. The app stores access and refresh tokens locally through platform secure storage.
5. Tokens are refreshed in the background before expiry when a refresh token is available.

### Codex (OpenAI / GitHub)
1. Open the **Codex** tab.
2. Click **+ Add Account**.
3. Choose OpenAI login, GitHub login, or manual token entry.
4. The app stores the extracted session/token data locally and refreshes monitored quota data through the refresh queue.

### Cursor
1. Install and log in to Cursor first.
2. Open the **Cursor** tab.
3. Click **Add Current Account**.
4. The app reads the local Cursor database and imports the current local session. No Cursor ID/password is required.

## Antigravity Model List

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

## Cursor Monitoring

- The Cursor tab reads the local Cursor database at `%APPDATA%\Cursor\User\globalStorage\state.vscdb`.
- If Cursor is not installed or not logged in, the app shows a setup guide and a button to open `https://cursor.com/`.
- Cursor cards show Composer context usage, remaining context, reset date, and bottom-hit status.
- Context usage keeps the highest recorded usage inside the same reset window so the card does not look falsely recovered before reset.
- Cursor card names are user-editable because Cursor session data may not expose a reliable email or nickname.

## Notes

- Version: `v1.0.6`
- Windows tray behavior is handled through the platform controller layer.
- The tray icon is shipped as `trayicon.ico` for more reliable Windows system tray rendering.

## Privacy

- Tokens and settings are stored locally.
- The app talks directly to provider endpoints.
- Review the source before using it with sensitive accounts.

## License

Distributed under the MIT License. See `LICENSE` for details.
