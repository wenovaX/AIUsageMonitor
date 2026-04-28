# AIUsageMonitor

> Antigravity와 Codex 사용량을 한 화면에서 확인하는 Windows 중심 AI 사용량 모니터링 앱

![AIUsageMonitor Header](Resources/Images/app_title.png)

## 개요

AIUsageMonitor는 여러 AI 계정의 사용량, 제한 상태, 리셋 시점을 한 곳에서 확인할 수 있는 .NET MAUI 데스크톱 앱입니다. 현재는 Windows 데스크톱 환경에 맞춰 최적화되어 있습니다.

## 미리보기

| Antigravity (Google) | Codex (OpenAI/GitHub) |
| :---: | :---: |
| ![Antigravity Preview](Preview/Preview-Antigravity.png) | ![Codex Preview](Preview/Preview-Codex.png) |

## 다운로드

최신 빌드는 Releases 페이지에서 받을 수 있습니다.

## 주요 기능

### 멀티 서비스 지원
- Antigravity 계정 및 모델 사용량 추적
- Codex 세션 제한과 주간 제한 모니터링
- 여러 계정을 한 대시보드에서 통합 관리

### Windows tray 워크플로우
- 트레이 아이콘 표시
- 좌클릭과 더블클릭으로 창 복원
- 닫기 시 트레이로 보내기 / 종료 선택
- 닫기 동작 기억하기

### 새로고침과 모니터링
- 헤더에서 전체 새로고침
- 현재 탭 기준 `F5` 전체 새로고침
- 제한된 동시성 기반 백그라운드 refresh queue
- 네트워크 오류를 고려한 재시도 흐름

### 프라이버시와 사용성
- 화면 공유용 Anonymous 모드
- Antigravity 모델 목록 수동 관리
- Google / Codex / Settings / About 탭 구성

## Antigravity 모델 목록

- 앱 시작 시 기본 모델 목록과 순서를 사용합니다.
  - `Gemini 3.1 Pro (High)`
  - `Gemini 3.1 Pro (Low)`
  - `Gemini 3 Flash`
  - `Claude Sonnet 4.6 (Thinking)`
  - `Claude Opus 4.6 (Thinking)`
  - `GPT-OSS 120B (Medium)`
- `Update Model List`는 이미 받아온 Antigravity quota 데이터 기준으로 모델을 추가합니다.
- 새로 발견된 모델은 기본적으로 `OFF` 상태로만 추가됩니다.
- `Set to Default`는 기본 모델 목록으로 되돌립니다.
- 현재 데이터에 없는 항목은 삭제하지 않고 Settings에서 `Missing`으로 표시합니다.

## 요구 사항

- .NET 10.0 SDK
- .NET MAUI workload가 포함된 Visual Studio
- Windows 10/11

## 소스에서 실행

1. 저장소를 클론합니다.
2. Visual Studio 에서 `AIUsageMonitor.sln`을 엽니다.
3. NuGet 패키지를 복원합니다.
4. `Windows Machine` 대상으로 실행합니다.

## 인증

### Antigravity (Google)
1. Open the Antigravity tab.
2. Click **+ Add Account**.
3. The Google OAuth flow opens in an in‑app browser.
4. After authorisation, the app securely stores the access and refresh tokens in `SecureStorage` (encrypted by the OS).
5. A background scheduler automatically refreshes the tokens 5 minutes before expiry, using a limited‑concurrency queue to avoid rate‑limit issues.

### Codex (OpenAI / GitHub)
1. Open the Codex tab.
2. Click **+ Add Account**.
3. Choose OpenAI login, GitHub (Copilot) login, or manual token entry.
4. For OpenAI, a WebView loads the ChatGPT login page; the app extracts the `accessToken` via injected JavaScript.
5. Extracted tokens are saved securely; a background refresh scheduler renews them before expiration (if a refresh token is available). For GitHub, a device‑code flow is used with similar secure storage.

## 참고

- 버전: `v1.0.5`
- Windows tray 동작은 platform controller 계층에서 관리합니다.
- tray 아이콘은 Windows 호환성을 위해 `trayicon.ico`로 배포합니다.

## 개인정보 및 보안

- 토큰과 설정은 로컬에 저장됩니다.
- 앱은 각 서비스 제공자 엔드포인트와 직접 통신합니다.
- 민감한 계정에 사용하기 전 소스를 검토하는 것을 권장합니다.

## 라이선스

MIT License를 따릅니다. 자세한 내용은 `LICENSE`를 확인해주세요.
