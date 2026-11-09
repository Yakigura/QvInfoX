[日本語](README.md) | [English](README.en.md) | [한국어](README.ko.md)

# Qv Scene Setup

A scene-specific installation record used by QvFastSync and QvInfoX. The record GameObject has the `EditorOnly` tag and is excluded from builds.

It records GameObjects and Components created during installation, as well as the before and after serialized values and prefab override state of existing objects. On removal, only values unchanged since installation are restored. Values changed by the user are shown as conflicts and can be forcibly restored with `-qvForce 1` in batch mode.

Removal stops for old scenes without a record because they cannot be removed safely.

## Installation with VCC

1. Add `https://yakigura.github.io/vpm-qvinfox/index.json` as a package repository in VRChat Creator Companion.
2. Install **Qv Scene Setup** in your project. It is automatically installed as a dependency when installing QvInfoX.

Qv Scene Setup itself does not depend on QvPen or QvInkPreview. When installing QvInfoX, QvPen and QvInkPreview are resolved automatically as dependencies from available repositories. QvPen is not included in the Yakigura listing, so also configure a repository that provides QvPen.