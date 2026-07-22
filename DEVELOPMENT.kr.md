# AIUsageMonitor 개발자 문서

이 문서는 사용자용 README에 길게 넣기 어려운 구현 세부 사항을 정리합니다.

[English developer notes](DEVELOPMENT.md) · [사용자 README](README.kr.md)

## 범위

**AIUsageMonitor는 쿼터를 모니터링합니다. 서비스 내부의 계정 전환을 자동화하지 않습니다.**

앱은 모니터링에 필요한 자격 정보를 보관하고,
provider endpoint를 호출한 뒤 반환된 사용량 상태를 화면에 표시합니다.<br>
실제 제품 사용과 계정 전환은 Google, ChatGPT/Codex, Cursor 안에서 직접 처리합니다.

## 기술 스택

- .NET 10
- .NET MAUI, 주 대상은 Windows
- Windows의 WinUI 3 및 WebView2
- 트레이 연동용 `H.NotifyIcon`
- Cursor 로컬 상태 DB 읽기용 `Microsoft.Data.Sqlite`

## 주요 영역

- `MainPage.xaml` / `MainPage.xaml.cs`: 대시보드 UI, 새로고침 큐, 계정 workflow
- `Services/*ApiService.cs`: provider 요청과 응답 parsing
- `Services/*AccountManagerService.cs`: 계정 collection과 persistence
- `Services/TokenStorageService.cs`: 암호화된 모니터링 토큰 저장
- `Services/AppDataPaths.cs`: 실행 파일 기준 local path와 legacy migration
- `PlatformImplementations/WindowsController.cs`: window, tray, shutdown 처리

## 로컬 데이터 경계

Runtime data는 실행 파일 옆에 둡니다.

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

중요한 동작:

- 토큰은 account JSON model에서 제외하고 `tokens.json`에 암호화해서 저장합니다.
- Cursor sensitive field는 저장 전에 암호화합니다.
- WebView2 cookies/cache는 `userdata/webview`를 사용합니다.
- 알려진 legacy AppData, SecureStorage, single-file extraction data는 발견 시 migration합니다.
- `userdata/`, `build/`, `bin/`, `obj/`는 Git에서 제외되어야 합니다.
- `userdata/`, `build/`, `.git/`, `.vs/`는 publish input에서도 제외되어야 합니다.

Local encryption은 실수로 인한 노출을 줄이는 장치입니다.<br>
이미 같은 OS user 권한으로 실행 중인 malware에 대한 방어책은 아닙니다.

실행 파일 옆 `userdata` 구조는 의도적인 risk-management 선택입니다.<br>
AppData 같은 system profile path는 편하지만 예측 가능한 위치입니다.<br>
침해 이후 session-adjacent app data를 확인, 이동, 백업, 삭제, 격리하기 쉽게 하려고
앱의 trust boundary를 실행 파일 옆에 보이게 둡니다.

## 새로고침 모델

- 계정 새로고침은 global/provider별 bounded queue를 사용합니다.
- 수동 새로고침은 background 새로고침보다 높은 priority를 받습니다.
- 종료 중에는 background loop와 pending UI callback을 cancel합니다.
- 계정 삭제와 토큰 저장은 UI thread deadlock을 피하기 위해 async로 유지해야 합니다.

## Codex 진단

Codex usage endpoint는 가끔 바뀝니다.<br>
Debug build에서는 아래 환경 변수로 raw usage JSON을 출력할 수 있습니다.

```text
AIUSAGEMONITOR_CODEX_RAW_LOG=1
```

Visual Studio `Windows Machine` profile은 이 변수를 활성화합니다.<br>
출력은 `Debug.WriteLine`을 통해 debugger로만 나갑니다.<br>
Release binary에서는 출력되지 않습니다.<br>
Authorization header와 access token은 logging하지 않습니다.

quota policy는 label만 보고 추론하지 않습니다.<br>
예를 들어 Free response가 현재 30-day `limit_window_seconds`를 내려줄 수 있습니다.<br>
API evidence 또는 반복 관찰이 없으면 weekly reset을 hardcode하지 않습니다.

## 빌드

기본 검증:

```powershell
dotnet restore
dotnet build AIUsageMonitor.csproj -f net10.0-windows10.0.19041.0 -c Release
```

단일 실행 파일 빌드:

```powershell
.\build-as-binary.bat
```

스크립트는 아래 결과물을 생성합니다.

```text
build/Release_yyyyMMdd_HHmmss/
  AIUsageMonitor.exe
  AIUsageMonitor-v{version}-win-x64-single-file-yyyyMMdd_HHmmss.zip
```

binary는 self-contained, compressed이며 Windows CoreCLR runtime을 사용하도록 `UseMonoRuntime=false`로 설정합니다.<br>
release zip 안에는 `AIUsageMonitor.exe` 하나만 들어갑니다.

build artifact가 publish input에 들어가면 안 됩니다.<br>
`build/`가 default item discovery에 포함되면 이전 release zip이 다음 single-file executable 안에
다시 묶여 binary가 비정상적으로 커질 수 있습니다.

## 커밋 전 확인

- Windows Release target을 build합니다.
- `userdata`, raw log, token, `.vs`, `bin`, `obj`, `build`가 staged되지 않았는지 확인합니다.
- provider response model 변경과 UI 변경은 따로 검토합니다.
- raw diagnostic logging은 opt-in 상태를 유지합니다.
