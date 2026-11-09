[日本語](README.md) | [English](README.en.md) | [한국어](README.ko.md)

# QvInfoX

QvInfoX는 QvPen 월드를 위한 무료 비침습형 모니터입니다. 플레이어 동기화 진행률, 전체 완료 예상 시간, 펜별 상세 정보와 현재 잉크 미리보기를 표시합니다. 모니터는 일본어, 영어, 한국어를 지원합니다. QvInfoX는 QvPen 자체를 수정하지 않습니다.

## 설치 전: Cyan PlayerObjectPool 저장소 추가

QvInfoX는 타사 패키지인 **Cyan PlayerObjectPool** (`com.cyan.playerobjectpool`)에 의존합니다. 이 패키지는 VCC 기본 목록과 Yakigura 저장소에 포함되어 있지 않습니다. 의존성을 해결하려면 먼저 해당 저장소를 VCC에 추가해야 합니다.

1. VRChat Creator Companion의 Settings > Packages > Add Repository에서 `https://cyanlaser.github.io/CyanPlayerObjectPool/index.json`을 패키지 저장소로 추가합니다.
2. 아래에 설명된 대로 Yakigura 저장소를 추가하고 QvInfoX를 설치합니다. QvInkPreview와 Qv Scene Setup은 Yakigura 저장소에서 자동 설치됩니다.

## VCC로 설치

1. VRChat Creator Companion에 `https://yakigura.github.io/vpm-qvinfox/index.json`을 패키지 저장소로 추가합니다.
2. 프로젝트에 **QvInfoX**를 설치합니다. 구성된 저장소에서 패키지를 사용할 수 있다면 VCC가 QvPen, QvInkPreview, Qv Scene Setup 및 기타 선언된 의존성을 자동으로 해결합니다. QvPen은 Yakigura 목록에 포함되지 않으므로 이를 제공하는 저장소도 설정하세요.

## 요구 사항

- Unity 2022.3 및 VRChat Worlds SDK (`com.vrchat.worlds`와 `com.vrchat.base`, 선언된 메이저 버전 범위 내 3.9.0 이상)
- QvPen (`net.ureishi.qvpen` 3.3.0 이상)
- QvInkPreview (`com.yakigura.qvinkpreview` 1.0.0 이상)와 Qv Scene Setup (`com.yakigura.qvscenesetup` 1.0.0 이상)
- Cyan PlayerObjectPool (`com.cyan.playerobjectpool`, 선언된 메이저 버전 범위 내 1.1.2 이상)

정확한 의존성 버전 범위는 `package.json`을 참조하세요. 2026-11-04에 빌드와 실기기 확인을 통해 QvHold의 VR ON/OFF 전환과 데스크톱의 항상 유지 동작을 확인했으나 사용 기기는 기록되지 않았습니다. 이후 변경 사항의 컴파일 및 런타임 동작과 새 프로젝트에 VPM 저장소를 통한 설치는 아직 검증되지 않았습니다.

## 월드에 QvInfoX 추가

1. VPM 클라이언트로 패키지와 의존성을 설치합니다. 로컬 복사본을 사용하는 경우 해결된 의존성과 함께 `Packages/com.yakigura.qvinfox`를 Unity 프로젝트에 추가합니다.
2. QvPen이 포함된 저장된 월드 씬을 열고 **Tools → Yakigura → QvInfoX → シーンに追加・更新 (Auto Setup)**을 선택합니다.
3. Console에서 설정 결과와 경고를 확인합니다. 안내가 나오면 Unity 스크립트 컴파일이 끝난 뒤 **シーンに追加・更新 (Auto Setup)**을 다시 실행합니다.
4. 씬을 저장하고 월드를 빌드합니다. 자동 설정은 펜 상세 정보용으로 월드에 고정된 `QvInfoX_WorldState` 하나, 헤더와 진행률용 작은 `QvInfoX_MasterTotals` 하나, 모니터, Cyan PlayerObjectPool, 감지된 각 펜의 Indicator를 생성합니다.

새 모니터는 첫 번째 스폰 지점 앞 2 m, 스폰 높이 + 0.9 m에 배치됩니다. 스폰 지점이 없으면 (0, 0.9, 2)를 사용하고 재실행 시 기존 모니터 위치를 유지합니다.

**シーンから取り除く (Remove from Scene)**은 씬의 설치 기록을 사용하며 QvPalette 등 다른 도구에서 참조하는 `QvInkPreview_*` 오브젝트를 보존합니다. 생성된 에셋은 다른 씬에서 계속 사용할 수 있습니다.

기존 QvInfoX 씬에서는 패키지를 갱신하고 **シーンに追加・更新 (Auto Setup)**을 다시 실행하여 Console을 확인한 다음 씬을 저장하고 다시 빌드합니다. 그러면 Master 집계가 PlayerSlot에서 단일 WorldState로 이전됩니다. 반복 실행해도 선택한 모니터 언어와 Indicator 끝 방향 설정은 유지됩니다.

## 월드 내 조작

펜 상세 정보는 자동 설정에서 저장한 계층 순서로 표시됩니다. 펜 계층을 변경한 뒤에는 자동 설정을 다시 실행하고 씬을 저장하세요.

모니터 오른쪽 아래에 패키지 버전이 표시됩니다. 패키지 갱신 후 이 표시를 갱신하려면 자동 설정을 다시 실행하세요.

모니터 중앙 아래 청록색 핸들을 잡아 패널을 이동합니다. 모니터 탭에서 플레이어 및 펜 상세 정보를 볼 수 있습니다. 공통 바닥글에는 새로고침, 언어, 위치 초기화, Indicator, QvHold 조작이 있습니다.

Indicator는 펜을 따라가며 그리거나 지우는 동안 숨겨집니다. 펜 모델의 끝 방향이 다르면 Indicator의 `tailAxis`를 `NegativeY`, `NegativeZ` 또는 `Custom`으로 설정하고 Inspector에서 `positionOffset`을 조정하세요. 미리보기 배치는 클라이언트에서 육안 확인이 필요합니다.

## QvHold

**ペン保持 / Hold pens / 펜 유지**는 사용자별 로컬 기능이며 Pickup v1.0을 지원합니다. 원래 QvPen 펜은 AutoHold `Sometimes`인 v1.0을 사용하므로 Pickup 이전이 필요하지 않습니다. 런타임에서 설정을 바꿀 수 있는 사용자는 VR 사용자뿐입니다. **ON은 모든 대상에 AutoHold `Yes`, OFF는 `No`를 요청**하며 원래 `Yes`인 펜도 포함됩니다. 데스크톱에서는 항상 유지가 활성화되고 QvHold가 AutoHold에 쓰지 않으며 체크박스는 비활성화되고 **デスクトップは常にホールド / Desktop: always hold / 데스크톱: 항상 홀드**가 표시됩니다. 원래 값은 저장하거나 복원하지 않습니다. 잡고 있는 펜의 변경은 놓을 때까지 대기하며 체크박스는 최신 요청을 따르고 **手放すと反映 / Applies after release / 놓으면 적용**이 지연을 나타냅니다. 강제 Drop이나 그리기/Use 이벤트 변경은 없습니다. 방문 간 설정은 저장되지 않습니다(PlayerData 없음).


`QvInfoX/QvInfoX_Monitor/HoldController`의 **入室時の初期状態 / Initial state on entry / 입장 시 초기 상태** (`initialState`, 정수 0–2)는 다음을 제공합니다.

| 값 | 초기 상태 | 입장 시 동작 |
| --- | --- | --- |
| **0 (기본값)** | シーンの設定のまま / Keep scene settings / 씬 설정 유지 | 아무것도 쓰지 않고 각 현재 AutoHold를 UI에 읽습니다. 모두 `Yes`면 ON, 모두 `Yes`가 아니면 OFF, 혼합이면 선택 해제하고 **混在（押すとON） / Mixed (click: ON) / 혼합 (누르면 ON)**을 표시합니다. `Yes` 이외 값은 정규화하지 않고 OFF로 표시합니다. |
| 1 | 保持 ON / Hold ON / 유지 ON | `Yes`를 요청하며 잡고 있는 펜은 대기합니다. |
| 2 | 保持 OFF / Hold OFF / 유지 OFF | `No`를 요청하며 잡고 있는 펜은 대기합니다. |

씬 설정 모드에서 값이 혼합된 경우 첫 클릭은 모든 대상에 ON, 다음 클릭은 OFF를 요청합니다. 모두 `Yes`이면 첫 클릭에 OFF를 요청합니다. 상태 줄의 **QvHold・自分のみ / QvHold · Local only / QvHold · 나에게만 적용**에 ON/OFF 또는 혼합이 추가됩니다. 대기는 혼합보다 우선하며 요청된 ON/OFF가 표시됩니다. 리스폰 및 Controller 재활성화는 현재 요청 또는 요청 전의 변경되지 않은 씬 설정 모드를 유지하며 입장 기본값을 다시 적용하지 않습니다.

자동 설정은 같은 GameObject에 `QvPen_Pen`과 SDK3 `VRCPickup`이 있고 Pickup이 **v1.0 또는 v1.1**인 씬 인스턴스만 연결합니다. 비활성 펜은 포함하고 지우개, 다른 도구, 모니터 Pickup은 제외합니다. 호환 대상이 없으면 Toggle은 OFF이고 비활성화됩니다. 자동 설정은 호환 및 건너뛴 수, 건너뛴 각 펜의 계층 경로와 이유, 복구 절차를 Console에 표시하며 별도의 검증이나 빌드 요구 사항은 추가하지 않습니다. 기존 v1.0 및 v1.1 Pickup은 정규화하지 않습니다.

대상을 선택하려면 `QvInfoX/QvInfoX_Monitor/HoldController`에서 **Selected Pens Only**를 활성화하고 **Selected Pens**에 QvPen 펜 컴포넌트를 지정한 뒤 자동 설정을 다시 실행하고 저장하세요. 기본값은 호환되는 모든 펜입니다. 반복 설정 시 선택과 입장 모드가 유지됩니다. 설정은 입장 모드(씬 설정의 혼합 값 포함)를 미리 보여 주지만 런타임 AutoHold 값을 적용하지 않습니다. Editor 호출자는 `QvInfoXSetupOptions.HoldInitialState`를 설정하거나 `PerformSetup` 또는 `SetupMonitor`에 `holdInitialState: 0/1/2`, `QvInfoX_HoldSetup.Setup`에 `initialState: 0/1/2`를 전달할 수 있습니다. 생성 인수는 기존 Controller를 덮어쓰지 않습니다. 펜을 추가·제거하거나 선택 또는 버전을 변경한 뒤에는 설정을 다시 실행하세요. 프로그램 에셋과 UI 참조는 기존 설치기가 생성하므로 해당 Undo/Redo 및 제거 기능을 사용하세요.

**기존 bool에서 Controller 입장 설정 업그레이드(Pickup 버전과 무관):** 새 입장 모드를 편집하기 전에 자동 설정을 한 번 실행하고 저장 및 재빌드하세요. 직렬화된 `initiallyEnabled` bool은 호환성을 위해 숨겨진 채 유지됩니다. 이전 `true`는 모드 1(ON)로 한 번 이전되며 이전 `false`는 모드 0(씬 설정)에 대응하여 기존의 입장 시 무기록 동작을 유지합니다. 설정이 이전 bool을 처리하므로 이후 편집한 세 모드의 설정은 반복 설정 후에도 유지됩니다. 이전 전 런타임에서도 `initialState`가 0이면 이전 `true`를 ON으로 취급하고 명시적인 모드 1 또는 2가 우선합니다. 따라서 이전하지 않은 이전 값 `true`인 Controller에서 모드 0만 설정해서는 이전 플래그를 덮어쓸 수 없습니다. 먼저 설정을 실행하세요. 기존 Editor bool 인수와 `QvInfoXSetupOptions.InitiallyEnableHold`는 소스 호환을 유지합니다. 새 모드가 0이면 `true`는 생성 시 모드 1을, `false`는 씬 설정을 요청합니다. 명시적인 새 모드 1 또는 2는 bool보다 우선합니다. bool에서 int로 자동 직렬화 변환된다고 가정하지 않습니다.

이 기능은 고정된 씬 참조와 항상 활성화된 Controller를 전제로 합니다. VR의 초기 ON/OFF는 잡고 있지 않은 대상에만 즉시 Yes/No를 적용하며 씬 설정 모드는 첫 요청까지 쓰지 않습니다. 연결된 비활성 펜도 포함됩니다. 잡고 있지 않은 Pickup은 비활성 상태에서도 설정할 수 있고 활성화 후에도 값을 유지합니다. 대기 중인 변경은 SDK 지연 이벤트 예약 없이 Update에서 0.25초 간격으로 확인합니다. 너무 이르거나 오래된 `_ProcessPending` 콜백은 아무 작업도 하지 않으며 Update 검사를 중지하지 않습니다. 탭 변경 및 플레이어/펜 리스폰 후에도 로컬 요청을 유지합니다. 동적으로 생성된 펜과 같은 펜의 AutoHold에 동시에 쓰는 스크립트는 지원하지 않습니다. 씬 설정 모드는 외부 쓰기를 지속적으로 감시하지 않습니다. Controller를 비활성화하면 대기 작업이 일시 중지되고 재활성화하면 재개됩니다. 삭제하면 마지막으로 로컬 적용된 값이 남으며 복원 과정은 없습니다. 대기 요청이 끝날 때까지 활성화해 두세요. 2026-11-04 실기기 확인은 VR ON/OFF 전환, 데스크톱 항상 유지, 빌드를 대상으로 했으며 사용 기기는 기록되지 않았습니다. ClientSim은 UI와 값을 확인하는 보조 수단일 뿐입니다.

## 문제 해결

- **메뉴가 없거나 컴파일 오류:** 패키지 의존성을 확인하고 Unity 컴파일 완료 후 자동 설정을 다시 실행하세요.
- **설정 경고:** **シーンに追加・更新 (Auto Setup)**을 다시 실행하고 활성 씬에 WorldState가 정확히 하나 있으며 생성된 Monitor와 PlayerPool이 활성화되어 있는지 확인하세요.
- **입장 후 합계 대기:** 유효한 현재 Master 스냅샷이 도착한 후에만 Master 합계를 표시합니다. 두 클라이언트로 중도 입장과 Master 인계를 확인하세요.
- **펜 수 불일치:** `masterPenStrokes` 배열 길이가 씬 펜 수와 다를 수 있습니다. 일치하는 보고가 도착할 때까지 펜 상세 정보는 대기 상태입니다.
- **문자 또는 레이아웃 문제:** VRChat 클라이언트에서 세 언어를 순환하며 긴 플레이어 이름을 확인하세요. Editor 검증만으로는 Quest의 글리프 표시를 증명할 수 없습니다.
