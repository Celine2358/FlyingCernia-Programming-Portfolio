# Flying Cernia — Core Gameplay Code
![Spinning Wheel Example](Assets/Flying Cernia 플레이 영상.gif)
Unity로 개발 중인 2D 횡스크롤 비행 액션 게임 **Flying Cernia**의 핵심 게임플레이 코드 모음입니다.

이 저장소는 전체 Unity 프로젝트나 플레이 가능한 빌드가 아니라, 제가 구현한 시스템의 구조와 문제 해결 방식을 보여주기 위해 선별한 **프로그래밍 포트폴리오용 소스 코드**입니다. 아트·사운드·씬·프리팹·서드파티 패키지는 포함하지 않습니다.

## 프로젝트 정보

| 항목 | 내용 |
| --- | --- |
| 장르 | 2D 횡스크롤 비행 액션 |
| 엔진 | Unity 2022.3.62f2 |
| 언어 | C# |
| 개발 상태 | Late Alpha / v0.2 |
| 현재 콘텐츠 | 세르니아 구름길 Easy, 2개 비행 구간 |
| 담당 | 게임플레이 및 시스템 프로그래밍 |

원본 프로젝트의 현재 흐름은 `MainScreen → LoadingScene → CerniaCloudRoad → Result`로 구성되어 있습니다. 이 저장소에서는 그중 조작감, 코스 진행, 맵 오브젝트, 고유 QTE, 결과 기록에 해당하는 코드만 공개합니다.

## 핵심 구현

### 1. 수식 기반 비행 조작

단순히 상승 속도를 대입하지 않고, 중력·상승 가속도·현재 속도에 대한 감쇠를 매 Fixed Update마다 계산합니다.

```text
verticalAcceleration = -gravity + riseAcceleration × input - verticalDrag × verticalVelocity
```

- 입력값을 즉시 0과 1로 바꾸지 않고 `MoveTowards`로 보간해 날개가 힘을 받는 느낌을 구현했습니다.
- 맵 길이와 목표 완주 시간을 이용해 X축 속도를 계산하므로 구간 크기가 달라도 플레이 시간을 일관되게 조정할 수 있습니다.
- 성장 배율과 Power Fly 배율을 런타임 속도에 함께 반영합니다.
- 비행, 피격 경직, 추락, 맵 전환, 일시정지 상태에서 물리와 입력이 충돌하지 않도록 상태별 진입·복귀 처리를 분리했습니다.

관련 코드: [Celine.cs](Source/Flight/Celine.cs), [Celine.Effects.cs](Source/Flight/Celine.Effects.cs), [CelineStats.cs](Source/Flight/CelineStats.cs), [CerniaFlyingMap.cs](Source/Flight/CerniaFlyingMap.cs)

### 2. 데이터 기반 코스와 맵 진행

`ScriptableObject`에 비행 물리, 거리 환산, 점수, 바람, 난이도와 해금 규칙을 보관하고 런타임 컨트롤러는 선택된 데이터로 구간을 구성합니다.

- 시작점과 끝점의 방향을 부호로 계산해 좌우 어느 방향의 코스에도 대응합니다.
- 현재 위치를 월드 거리와 미터 단위로 변환합니다.
- 지나온 거리의 차이만 점수에 더해 Power Fly 도중 속도 배율이 바뀌어도 이미 지난 구간이 중복 계산되지 않게 했습니다.
- 맵 전환 중에는 입력과 물리를 잠그고, 배경·거리·패턴을 교체한 뒤 같은 플레이 상태로 복귀합니다.

관련 코드: [CerniaFlightCourse.cs](Source/Course/CerniaFlightCourse.cs), [MapSequenceController.cs](Source/Course/MapSequenceController.cs)

### 3. 데이터와 동작을 분리한 맵 오브젝트

코인, 마력 결정, 회복 아이템, 장애물, QTE 장애물을 하나의 런타임 컴포넌트로 처리하되 수치와 접촉 규칙은 `MapObjectData`에 분리했습니다.

- 보상, 피해, 자석 반응, Power Fly 반응을 데이터로 설정합니다.
- 소비·미스·제거 상태를 구분해 같은 오브젝트가 두 번 처리되지 않게 했습니다.
- 장애물 파괴 점수의 지급 권한을 한 번만 획득하도록 해 중복 보상을 방지했습니다.
- 활성 QTE 대상만 별도 집합으로 관리해 가장 가까운 유효 대상을 탐색합니다.

관련 코드: [MapObjectData.cs](Source/World/MapObjectData.cs), [MapObject.cs](Source/World/MapObject.cs)

### 4. White Chain QTE와 Power Fly 연동

장애물을 지정한 뒤 제한 시간 안에 코어를 해결하면 장애물을 파괴하고 Power Fly로 이어지는 고유 시스템입니다.

- PC는 키보드 입력, 모바일은 월드 좌표 터치 방식으로 분기합니다.
- 게임 월드는 `Time.timeScale = 0`으로 멈추고 QTE 제한 시간과 연출은 `unscaledDeltaTime`으로 진행합니다.
- 이미 등장한 키와 직전 키의 가중치를 낮춰 같은 입력이 과도하게 반복되지 않게 했습니다.
- 성공 시 기본 시간, 남은 QTE 시간, 사용 가능한 마력을 합산해 Power Fly 시간을 계산합니다.
- Power Fly 연료로 사용할 마력을 예약해 다른 QTE가 같은 자원을 중복 소비하지 못하게 했습니다.

관련 코드: [WhiteChainController.cs](Source/WhiteChain/WhiteChainController.cs), [Celine.Effects.cs](Source/Flight/Celine.Effects.cs)

### 5. 실행 중 통계와 영구 기록 분리

플레이 중 계속 변하는 통계와 결과가 확정된 순간의 스냅샷을 분리했습니다.

- 피격 횟수, 누적 피해, QTE 성공·실패, 최단 해결 시간, 최장 Power Fly 시간을 한 Run 단위로 집계합니다.
- 코스와 난이도 ID를 기준으로 기록을 구분하고 점수·랭크·완주 여부·최근 순서로 Top 5를 정렬합니다.
- 저장 파일이 없거나 손상되었을 때 기본 데이터로 복구합니다.
- 로컬 HMAC과 XOR은 온라인 보안이 아니라 알파 단계의 단순 수정 감지와 난독화 용도로만 사용합니다.

관련 코드: [FlyingRunStats.cs](Source/Data/FlyingRunStats.cs), [FlyingResultRecord.cs](Source/Data/FlyingResultRecord.cs), [CelineDataManager.cs](Source/Data/CelineDataManager.cs)

## 추천 코드 읽기 순서

1. [CerniaFlyingMap.cs](Source/Flight/CerniaFlyingMap.cs) — 맵별 비행 규칙과 전진 속도 계산
2. [Celine.cs](Source/Flight/Celine.cs) — 실제 입력, 비행 수식, 상태 전환
3. [MapSequenceController.cs](Source/Course/MapSequenceController.cs) — 구간 진행, 거리와 점수 누적
4. [MapObjectData.cs](Source/World/MapObjectData.cs)와 [MapObject.cs](Source/World/MapObject.cs) — 데이터와 런타임 동작의 분리
5. [WhiteChainController.cs](Source/WhiteChain/WhiteChainController.cs) — QTE 상태 흐름과 플랫폼별 입력
6. [CelineDataManager.cs](Source/Data/CelineDataManager.cs) — 결과 정렬, 저장, 손상 데이터 복구

## 저장소 구조

```text
Source/
├─ Flight/       # 비행 물리, 캐릭터 상태, 능력치, 맵 설정
├─ Course/       # 코스 데이터, 구간 전환, 거리와 점수
├─ World/        # 데이터 기반 아이템·장애물 처리
├─ WhiteChain/   # 고유 QTE 시스템
└─ Data/         # Run 통계, 결과 스냅샷, 로컬 저장
```

## 사용 기술

- Unity 2D Physics
- Unity Input System
- ScriptableObject 기반 게임 데이터
- Cinemachine
- DOTween
- Spine-Unity
- JSON 직렬화, HMAC-SHA256, 로컬 파일 저장

## 공개 범위와 실행 안내

이 저장소에는 코드 이해에 필요한 대표 파일만 포함되어 있어 단독으로 컴파일하거나 실행할 수 없습니다. 실제 프로젝트에서 사용하는 UI, 씬 참조, 프리팹, 생성된 Input Actions, 아트·사운드 리소스와 서드파티 코드는 의도적으로 제외했습니다.

코드는 **2026-09-04 개발본을 기준으로 선별한 스냅샷**입니다.

## 다음 개선 목표

- White Chain의 규칙 계산과 Unity 연출 코드를 분리해 테스트 가능한 순수 C# 로직으로 정리
- 거리 점수, 랭크, Top 5 기록 정렬에 대한 자동화 테스트 추가
- 로컬 저장 포맷의 버전별 마이그레이션 절차 명확화

