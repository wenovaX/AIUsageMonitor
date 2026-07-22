# AIUsageMonitor

> 가성비가 중요한 개인 AI 개발자를 위한 다중 계정 쿼터 모니터.

형편에 맞춰 여러 AI 계정을 돌려 쓰더라도,<br>
남은 사용량과 리셋 시간을 확인하는 일까지 번거로울 필요는 없습니다.

로그인과 실제 작업은 각 서비스에서 직접 하고,<br>
AIUsageMonitor에서는 계정별 쿼터와 상태만 한 화면에서 확인합니다.

**자동 계정 전환 도구가 아닙니다.**

다중 계정 도구를 쓰다가 세션이 자꾸 풀려,<br>
결국 매번 수동 로그인하게 된 경험에서 시작했습니다.<br>
그래서 로그인 자동화보다 쿼터 모니터링에 집중합니다.

[English README](README.md) · [개발자 문서](DEVELOPMENT.kr.md)

## 확인할 수 있는 것

- Antigravity 계정 및 모델별 쿼터
- Codex 사용 한도와 리셋 시간
- Cursor 사용량과 로컬 Composer 컨텍스트 상태
- 여러 계정을 모아 보는 Windows 대시보드
- 수동·대기열·백그라운드 새로고침
- 트레이 및 선택형 Slack 알림
- 화면 공유용 익명 모드

## 미리보기

| Antigravity | Codex |
| :---: | :---: |
| ![Antigravity Preview](Preview/Preview-Antigravity.png) | ![Codex Preview](Preview/Preview-Codex.png) |

## 사용 방식

1. 모니터링할 계정을 등록합니다.
2. Google, ChatGPT/Codex, Cursor는 평소처럼 직접 로그인해서 사용합니다.
3. 남은 쿼터와 리셋 시간을 한 번에 보고 싶을 때 AIUsageMonitor를 새로고침합니다.

서비스 내부의 계정 전환을 자동화하지 않습니다.<br>
각 서비스의 로그인 화면도 대체하지 않습니다.

## 로컬 데이터

계정 메타데이터, 암호화된 모니터링 토큰,<br>
설정, WebView 데이터는 실행 파일 옆 `userdata`에서 관리합니다.

이 구조는 의도적인 선택입니다.<br>
시스템 프로필 경로는 편하지만,<br>
침해 이후 긁히기 쉬운 흔한 위치이기도 합니다.<br>
그래서 앱의 데이터 경계를 실행 파일 옆에 보이게 두고,<br>
사용자가 직접 복사·삭제·격리할 수 있도록 했습니다.

```text
AIUsageMonitor.exe
userdata/
  data/
  webview/
```

`userdata`는 Git에서 제외됩니다.

## 다운로드 및 빌드

Releases 페이지에서 빌드를 받거나,<br>
.NET 10과 .NET MAUI 워크로드로 Windows target을 빌드합니다.

```powershell
dotnet build AIUsageMonitor.csproj -f net10.0-windows10.0.19041.0 -c Release
```

`build-as-binary.bat`를 실행하면 압축된 self-contained 단일 exe와,<br>
버전이 들어간 release zip이 함께 생성됩니다.

```text
build/Release_yyyyMMdd_HHmmss/
  AIUsageMonitor.exe
  AIUsageMonitor-v1.0.9-win-x64-single-file-yyyyMMdd_HHmmss.zip
```

zip 안에는 `AIUsageMonitor.exe` 하나만 들어갑니다.

## 개인정보 및 보안

- 앱 로컬 데이터는 실행 파일 옆 `userdata`에 둡니다.
- `build`, `userdata`, `.git`, `.vs`는 publish 입력에서 제외합니다.
- 각 서비스 요청은 해당 서비스 endpoint로 직접 전송합니다.
- Codex raw response 로그는 Debug 진단에서만 선택적으로 활성화합니다.
- 민감한 계정을 등록하기 전에 소스 검토를 권장합니다.

## 라이선스

MIT License. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.
