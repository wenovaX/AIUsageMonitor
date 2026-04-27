# AIUsageMonitor

> Antigravity와 Codex 사용량을 한곳에서 확인할 수 있는 프리미엄 AI 사용량 모니터링 앱

![AIUsageMonitor Header](Resources/Images/app_title.png)

## 개요

AIUsageMonitor는 여러 AI 계정의 사용량, 제한 상태, 리셋 윈도우를 한 화면에서 확인할 수 있는 .NET MAUI 기반 데스크톱 앱입니다. 현재는 Windows 데스크톱 사용에 맞춰 정리되어 있습니다.

## 미리보기

| Antigravity (Google) | Codex (OpenAI/GitHub) |
| :---: | :---: |
| ![Antigravity Preview](Preview/Preview-Antigravity.png) | ![Codex Preview](Preview/Preview-Codex.png) |

## 다운로드

최신 빌드는 Releases 페이지에서 받을 수 있습니다.

## 주요 기능

### 멀티 서비스 지원
- Antigravity 계정과 모델 사용량 추적
- Codex 세션 제한과 주간 쿼터 모니터링
- 여러 계정을 한 대시보드에서 통합 관리

### Windows 트레이 워크플로우
- 트레이 아이콘 지원
- 좌클릭 및 더블클릭으로 앱 복원
- 창 닫기 시 트레이로 보내기 / 종료 선택
- 닫기 팝업에서만 백그라운드 안내 알림 표시
- 선택 기억 기능으로 이후 동작 단순화

### 새로고침과 모니터링
- 헤더에서 전체 새로고침 실행
- 현재 탭 기준 `F5` 전체 새로고침 지원
- 제한된 동시성 기반 백그라운드 refresh queue
- 네트워크 오류를 고려한 재시도 흐름

### 프라이버시와 사용성
- 화면 공유용 Anonymous 모드
- 모델 필터 커스터마이징
- Google / Codex / Settings / About 탭 구성

## 요구 사항

- .NET 10.0 SDK
- .NET MAUI workload가 포함된 Visual Studio 2022
- Windows 10/11

## 소스에서 실행

1. 저장소를 클론합니다.
2. Visual Studio 2022에서 `AIUsageMonitor.sln`을 엽니다.
3. NuGet 패키지를 복원합니다.
4. `Windows Machine` 대상으로 실행합니다.

## 인증

### Antigravity (Google)
1. Antigravity 탭으로 이동합니다.
2. `+ Add Account` 버튼을 누릅니다.
3. 브라우저에서 Google OAuth 인증을 완료합니다.

### Codex (OpenAI / GitHub)
1. Codex 탭으로 이동합니다.
2. `+ Add Account` 버튼을 누릅니다.
3. OpenAI 로그인, GitHub 로그인, 수동 토큰 입력 중 하나를 선택합니다.

## 참고

- 버전: `v1.0.3`
- Windows tray 동작은 platform controller 계층에서 관리합니다.
- 트레이 아이콘은 Windows 호환성을 위해 `trayicon.ico`로 배포됩니다.

## 개인정보 및 보안

- 토큰과 설정은 로컬에 저장됩니다.
- 앱은 각 제공자 엔드포인트와 직접 통신합니다.
- 민감한 계정에 사용하기 전 소스를 검토하는 것을 권장합니다.

## 라이선스

MIT License를 따릅니다. 자세한 내용은 `LICENSE`를 확인해주세요.
