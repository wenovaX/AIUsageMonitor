# Release Notes

## v1.1.0

### Added

- Added English and Korean UI languages, with an option to follow the system language.
- Re-adding an existing account updates its current entry instead of creating a duplicate.
- Added a confirmation step before account removal and a direct re-authentication action for authentication errors.
- Added an option to remove models marked missing by the latest successful model refresh.
- Added a configurable account refresh concurrency limit, defaulting to two accounts at a time.

### Improved

- Refreshed the dashboard with a new title logo, responsive virtualized account lists, and tabs that load on demand.
- Reorganized settings with consistent alignment and spacing, and improved the visibility and focus behavior of controls.
- Updated the SQLite native runtime to SQLitePCLRaw 3 and SQLite 3.53.4, removing the vulnerable 2.1.11 native package chain.

## 한국어

### 추가

- 한국어·영어 UI와 시스템 언어 설정을 추가했습니다.
- 이미 등록된 계정을 다시 추가하면 중복 생성하지 않고 기존 항목을 갱신합니다.
- 계정 삭제 전에 확인을 거치며, 인증 오류 상세 화면에서 바로 다시 인증할 수 있습니다.
- 최근 모델 목록 새로고침에서 감지되지 않은 모델을 정리하는 기능을 추가했습니다.
- 계정 새로고침 동시 처리 수를 설정하는 옵션을 추가했습니다. 기본값은 2개입니다.

### 개선

- 새 타이틀 로고와 반응형 가상화 계정 목록을 적용하고, 탭 화면은 필요할 때 생성하도록 개선했습니다.
- 설정 화면의 정렬과 여백을 통일하고, 조절 버튼의 가시성과 포커스 표시를 개선했습니다.
- 취약한 SQLite 2.1.11 네이티브 패키지 구성을 제거하고 SQLitePCLRaw 3 및 SQLite 3.53.4로 업데이트했습니다.
