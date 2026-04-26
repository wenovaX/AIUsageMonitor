# 📡 AIUsageMonitor (AI 사용량 모니터)

> **프리미엄 AI 사용량 모니터링 대시보드**  
> Antigravity와 OpenAI Codex의 사용량 및 제한을 하나의 세련된 인터페이스에서 간편하게 추적하세요.

## ✨ 개요

AIUsageMonitor는 여러 계정과 모델을 관리하는 AI 파워 유저를 위한 .NET MAUI 기반 데스크톱 애플리케이션입니다. 사용량 백분율, 초기화 시간, 크레딧 잔액을 실시간으로 확인하여 중요한 작업 중 예기치 않게 제한에 걸리는 일을 방지합니다.

## 🚀 주요 기능

### 🏢 멀티 서비스 지원
- **Antigravity (Google)**: Gemini 3 Flash, 3.1 Pro (High/Low), Claude Opus/Sonnet 4.6, GPT-OSS 120B 모델 추적.
- **Codex (OpenAI/GitHub)**: 지능형 윈도우 정규화를 통해 세션별 제한 및 주간 쿼터 모니터링.

### 🎨 프리미엄 사용자 경험
- **모던 다크 UI**: 에메랄드와 스카이 블루 액센트가 가미된 세련된 다크 테마.
- **익명 모드 (Anonymous Mode)**: 개인정보(이름, 이메일)를 즉시 마스킹하여 프라이버시 보호 및 화면 공유 가능.
- **탭 인터페이스**: Google 모델, Codex 계정, 설정을 간편하게 전환.

### ⚙️ 완전한 커스터마이징
- **모델 필터**: 본인만의 추적 키워드와 표시 이름을 직접 정의.
- **유연한 정렬**: 모델 카드의 순서를 변경하여 중요한 모델을 우선적으로 배치.
- **자동 새로고침 및 재시도**: 설정 가능한 재시도 로직과 수동 새로고침 기능으로 데이터 신뢰성 확보.

## 🛠️ 설정 및 설치

### 요구 사항
- .NET 8.0 또는 10.0 SDK
- Windows 10/11

### 소스에서 빌드
1. 저장소를 클론(Clone)합니다.
2. Visual Studio 2022에서 `AIUsageMonitor.sln`을 엽니다.
3. NuGet 패키지를 복원합니다.
4. 빌드 및 실행 (대상: Windows Machine).

## 🔑 인증 가이드

### Antigravity (Google)
1. IDE에 Antigravity 확장이 설치되어 있고 로그인이 되어 있는지 확인하세요.
2. Antigravity 탭에서 **(+)** 버튼을 클릭합니다.
3. 브라우저에서 Google OAuth 인증을 완료합니다.

### Codex (OpenAI / GitHub)
1. **Codex** 탭으로 전환하고 **(+)** 버튼을 클릭합니다.
2. **OpenAI Login**: PKCE OAuth 흐름을 통해 OpenAI 계정에 직접 접근합니다.
3. **GitHub Login**: GitHub Copilot을 사용하는 경우 Device Flow를 통해 인증합니다.

## 🛡️ 개인정보 및 보안
- **로컬 저장**: 모든 계정 토큰과 설정은 사용자의 로컬 컴퓨터에만 저장됩니다.
- **직접 통신**: 앱은 중개 서버 없이 Google 및 OpenAI API와 직접 통신합니다.
- **오픈 소스**: 코드를 직접 검토하여 토큰이 안전하게 처리되는지 확인할 수 있습니다.

## 📄 라이선스
MIT 라이선스에 따라 배포됩니다. 자세한 내용은 `LICENSE`를 참조하세요.
