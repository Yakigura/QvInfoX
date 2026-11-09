[日本語](README.md) | [English](README.en.md) | [한국어](README.ko.md)

# Qv Scene Setup

QvFastSync와 QvInfoX가 사용하는 씬별 설치 기록입니다. 기록 GameObject는 `EditorOnly` 태그로 빌드에서 제외됩니다.

설치 시 생성한 GameObject와 Component, 기존 직렬화 값의 변경 전후 및 프리팹 오버라이드 상태를 기록합니다. 제거할 때는 설치 이후 변경되지 않은 값만 복원합니다. 사용자가 변경한 값은 충돌로 표시되며 배치 모드에서 `-qvForce 1`로 강제 복원할 수 있습니다.

기록이 없는 이전 씬은 안전하게 제거할 수 없으므로 제거 처리가 중단됩니다.

## VCC로 설치

1. VRChat Creator Companion에 `https://yakigura.github.io/vpm-qvinfox/index.json`을 패키지 저장소로 추가합니다.
2. 프로젝트에 **Qv Scene Setup**을 설치합니다. QvInfoX를 설치하면 의존성으로 자동 설치됩니다.

Qv Scene Setup 자체는 QvPen이나 QvInkPreview에 의존하지 않습니다. QvInfoX를 설치할 때는 사용 가능한 저장소에서 QvPen과 QvInkPreview를 의존성으로 자동 해결합니다. QvPen은 Yakigura 목록에 포함되지 않으므로 QvPen을 제공하는 저장소도 설정하세요.