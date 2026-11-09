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

정확한 의존성 버전 범위는 `package.json`을 참조하세요.

## 월드에 QvInfoX 추가

1. VPM 클라이언트로 패키지와 의존성을 설치합니다. 로컬 복사본을 사용하는 경우 해결된 의존성과 함께 `Packages/com.yakigura.qvinfox`를 Unity 프로젝트에 추가합니다.
2. QvPen이 포함된 저장된 월드 씬을 열고 **Tools → Yakigura → QvInfoX → シーンに追加・更新 (Auto Setup)**을 선택합니다.
3. Console에서 설정 결과와 경고를 확인합니다. 안내가 나오면 Unity 스크립트 컴파일이 끝난 뒤 **シーンに追加・更新 (Auto Setup)**을 다시 실행합니다.
4. 씬을 저장하고 월드를 빌드합니다. 자동 설정은 펜 상세 정보용으로 월드에 고정된 `QvInfoX_WorldState` 하나, 헤더와 진행률용 작은 `QvInfoX_MasterTotals` 하나, 모니터, Cyan PlayerObjectPool, 감지된 각 펜의 Indicator를 생성합니다.

**シーンから取り除く (Remove from Scene)**은 씬의 설치 기록을 사용하며 QvPalette 등 다른 도구에서 참조하는 `QvInkPreview_*` 오브젝트를 보존합니다. 생성된 에셋은 다른 씬에서 계속 사용할 수 있습니다.

기존 QvInfoX 씬에서는 패키지를 갱신하고 **シーンに追加・更新 (Auto Setup)**을 다시 실행하여 Console을 확인한 다음 씬을 저장하고 다시 빌드합니다. 그러면 Master 집계가 PlayerSlot에서 단일 WorldState로 이전됩니다. 반복 실행해도 선택한 모니터 언어와 Indicator 끝 방향 설정은 유지됩니다.

## 월드 내 조작

펜 상세 정보는 자동 설정에서 저장한 계층 순서로 표시됩니다. 펜 계층을 변경한 뒤에는 자동 설정을 다시 실행하고 씬을 저장하세요.

모니터 오른쪽 아래에 패키지 버전이 표시됩니다. 패키지 갱신 후 이 표시를 갱신하려면 자동 설정을 다시 실행하세요.

모니터 중앙 아래 청록색 핸들을 잡아 패널을 이동합니다. 모니터 탭에서 플레이어 및 펜 상세 정보를 볼 수 있습니다. 공통 바닥글에는 새로고침, 언어, 위치 초기화, Indicator, QvHold 조작이 있습니다.

Indicator는 펜을 따라가며 그리거나 지우는 동안 숨겨집니다. 펜 모델의 끝 방향이 다르면 Indicator의 `tailAxis`를 `NegativeY`, `NegativeZ` 또는 `Custom`으로 설정하고 Inspector에서 `positionOffset`을 조정하세요. 미리보기 배치는 클라이언트에서 육안 확인이 필요합니다.

## QvHold

**펜 유지(ペン保持 / Hold pens)**는 VR에서 펜을 계속 쥐고 있을지를 ON / OFF로 전환하는 기능입니다. 모니터의 체크박스로 전환합니다. 데스크톱에서는 원래의 QvPen과 같이 항상 펜을 쥐고 있습니다. 이 설정은 사용자별이며 다른 사람에게는 영향을 주지 않습니다.

## 문제 해결

- **메뉴가 없거나 컴파일 오류:** 패키지 의존성을 확인하고 Unity 컴파일 완료 후 자동 설정을 다시 실행하세요.
- **설정 경고:** **シーンに追加・更新 (Auto Setup)**을 다시 실행하고 활성 씬에 WorldState가 정확히 하나 있으며 생성된 Monitor와 PlayerPool이 활성화되어 있는지 확인하세요.
- **입장 후 합계 대기:** 유효한 현재 Master 스냅샷이 도착한 후에만 Master 합계를 표시합니다. 두 클라이언트로 중도 입장과 Master 인계를 확인하세요.
- **펜 수 불일치:** `masterPenStrokes` 배열 길이가 씬 펜 수와 다를 수 있습니다. 일치하는 보고가 도착할 때까지 펜 상세 정보는 대기 상태입니다.
- **문자 또는 레이아웃 문제:** VRChat 클라이언트에서 세 언어를 순환하며 긴 플레이어 이름을 확인하세요.
