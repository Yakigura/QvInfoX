[日本語](README.md) | [English](README.en.md) | [한국어](README.ko.md)

# QvInfoX

QvInfoX is a free, non-invasive monitor for QvPen worlds. It shows player sync progress, an overall ETA, per-pen details, and a preview of the pen's current ink. The Monitor supports Japanese, English, and Korean. QvInfoX does not modify QvPen itself.

## Before installing: add the Cyan PlayerObjectPool repository

QvInfoX depends on **Cyan PlayerObjectPool** (`com.cyan.playerobjectpool`), a third-party package that is **not** in the default VCC list and is **not** hosted in the Yakigura repository. Add its repository to VCC first, otherwise VCC cannot resolve the dependency:

1. In VRChat Creator Companion, add `https://cyanlaser.github.io/CyanPlayerObjectPool/index.json` as a package repository (Settings > Packages > Add Repository).
2. Then add the Yakigura repository and install QvInfoX as described below. QvInkPreview and Qv Scene Setup are installed automatically from the Yakigura repository.

## Installation with VCC

1. In VRChat Creator Companion, add `https://yakigura.github.io/vpm-qvinfox/index.json` as a package repository.
2. Install **QvInfoX** in your project. VCC resolves QvPen, QvInkPreview, Qv Scene Setup, and the other declared dependencies automatically when their packages are available from configured repositories. QvPen is not included in the Yakigura listing, so configure a repository that provides it.

## Requirements

- Unity 2022.3 and the VRChat Worlds SDK (`com.vrchat.worlds` and `com.vrchat.base`, version 3.9.0 or newer within the declared major range)
- QvPen (`net.ureishi.qvpen` 3.3.0 or newer)
- QvInkPreview (`com.yakigura.qvinkpreview` 1.0.0 or newer) and Qv Scene Setup (`com.yakigura.qvscenesetup` 1.0.0 or newer)
- Cyan PlayerObjectPool (`com.cyan.playerobjectpool` 1.1.2 or newer within the declared major range)

The exact dependency ranges are in `package.json`.

## Add QvInfoX to a world

1. Install the package and its dependencies through your VPM client. For a local copy, add `Packages/com.yakigura.qvinfox` to the Unity project alongside resolved dependencies.
2. Open the saved world scene containing QvPen. Choose **Tools → Yakigura → QvInfoX → シーンに追加・更新 (Auto Setup)**.
3. Check the Console for the setup result and warnings. After Unity finishes script compilation, rerun **シーンに追加・更新 (Auto Setup)** if the Console asks you to.
4. Save the scene, then build the world. シーンに追加・更新 (Auto Setup) creates one world-fixed `QvInfoX_WorldState` for pen details, one small `QvInfoX_MasterTotals` for the header and progress, one Monitor, a Cyan PlayerObjectPool, and an Indicator for each detected pen.

New Monitors appear 2 m in front of the first spawn at spawn height + 0.9 m; without a spawn they use (0, 0.9, 2), and existing Monitor positions are preserved on rerun.

**シーンから取り除く (Remove from Scene)** uses the scene's install record and preserves `QvInkPreview_*` objects referenced by other tools such as QvPalette. Generated assets remain available to other scenes.

For an existing QvInfoX scene, update the package, rerun **シーンに追加・更新 (Auto Setup)**, check the Console, then save and rebuild the scene. This migrates the Master aggregate from PlayerSlot to the single WorldState. Repeated シーンに追加・更新 (Auto Setup) preserves the selected Monitor language and Indicator tail settings.

## In-world controls

Pen details follow the hierarchy order saved by シーンに追加・更新 (Auto Setup); rerun シーンに追加・更新 (Auto Setup) and save the scene after changing the pen hierarchy.

The Monitor shows the package version at its lower-right edge; rerun シーンに追加・更新 (Auto Setup) after updating the package to refresh it.

Grab the teal handle below the center of the Monitor to move the panel. Use the Monitor tabs for players and pen details. The shared footer has Refresh, language, position reset, Indicator, and QvHold controls.

The Indicator follows its pen and hides while drawing or erasing. If a pen model has a different tail direction, set its Indicator `tailAxis` to `NegativeY`, `NegativeZ`, or `Custom`, then adjust `positionOffset` in the Inspector. Preview placement needs a visual check in the client.

## QvHold

**Hold pens (ペン保持 / 펜 유지)** switches whether the pen stays in your hand in VR, ON or OFF, with the checkbox on the Monitor. On desktop the pen is always held, as with the original QvPen. The setting is per user and does not affect other players.

## Troubleshooting

- **Missing menu or compiler errors:** verify the package dependencies and wait for Unity to finish compiling before rerunning シーンに追加・更新 (Auto Setup).
- **Setup warning:** rerun **シーンに追加・更新 (Auto Setup)**. Check that the active scene has exactly one WorldState and that the generated Monitor and PlayerPool are enabled.
- **Pending totals after joining:** Master totals are shown only after a valid current-Master snapshot arrives. Use two clients to check late join and Master handoff.
- **Pen count mismatch:** A `masterPenStrokes` array length can differ from the scene pen count. Pen details remain pending until a matching report arrives.
- **Text or layout issue:** cycle all three languages and check long player names in a VRChat client.
