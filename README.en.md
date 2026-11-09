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

The exact dependency ranges are in `package.json`. On 2026-11-04, a build and real-device checks confirmed QvHold VR ON/OFF switching and desktop always-hold behavior; the devices were not recorded. Compilation and runtime behavior of subsequent changes have not been verified. Installation through a VPM repository into a new project has not yet been verified.

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

**ペン保持 / Hold pens / 펜 유지** is local to each user and works with Pickup v1.0. The original QvPen pens use v1.0 with AutoHold `Sometimes`; no Pickup migration is required. Only VR users can switch the setting at runtime: **ON requests AutoHold `Yes`; OFF requests `No` for every target**, including pens originally set to `Yes`. On desktop, holding is always enabled, QvHold never writes AutoHold, and the checkbox is disabled with **デスクトップは常にホールド / Desktop: always hold / 데스크톱: 항상 홀드** displayed. Original values are not saved or restored. Changes to a held pen wait until it is released; the checkbox follows the latest request and **手放すと反映 / Applies after release / 놓으면 적용** indicates the delay. QvHold never forces a Drop or changes drawing/Use events. No preference is saved between visits (no PlayerData).


On `QvInfoX/QvInfoX_Monitor/HoldController`, **入室時の初期状態 / Initial state on entry / 입장 시 초기 상태** (`initialState`, integer 0–2) offers:

| Value | Initial state | Behavior on entry |
| --- | --- | --- |
| **0 (default)** | シーンの設定のまま / Keep scene settings / 씬 설정 유지 | Write nothing. Read each current AutoHold for the UI: all `Yes` = checked ON; all non-`Yes` = unchecked OFF; mixed = unchecked with **混在（押すとON） / Mixed (click: ON) / 혼합 (누르면 ON)**. Non-`Yes` values are displayed as OFF without normalization. |
| 1 | 保持 ON / Hold ON / 유지 ON | Request `Yes`, deferring held pens. |
| 2 | 保持 OFF / Hold OFF / 유지 OFF | Request `No`, deferring held pens. |

In mixed Scene mode, the first click requests ON for every target; the next click requests OFF. With all `Yes`, the first click requests OFF. The status line adds ON/OFF or mixed to **QvHold・自分のみ / QvHold · Local only / QvHold · 나에게만 적용**; pending takes precedence over mixed and shows the requested ON/OFF. Respawn and Controller re-enabling retain the current request, or the untouched Scene mode before any request; they do not reapply entry defaults.

シーンに追加・更新 (Auto Setup) wires only scene instances with `QvPen_Pen` and SDK3 `VRCPickup` on the same GameObject, with Pickup **v1.0 or v1.1**. Inactive pens are included; erasers, other tools, and the Monitor Pickup are excluded. With zero compatible targets the Toggle is OFF and disabled. シーンに追加・更新 (Auto Setup) reports compatible and skipped counts, each skipped pen's hierarchy path and reason, and recovery steps in the Console, without adding validation or build requirements. Existing v1.0 and v1.1 Pickups are never normalized by setup.

For selected targets, edit `QvInfoX/QvInfoX_Monitor/HoldController`: enable **Selected Pens Only**, populate **Selected Pens** with QvPen pen components, then rerun シーンに追加・更新 (Auto Setup) and save. The default is all compatible pens. Repeated setup preserves both the selection and the entry mode. Setup previews the entry mode (including Scene mixed values) without applying runtime AutoHold values. Editor callers can set `QvInfoXSetupOptions.HoldInitialState`, pass `holdInitialState: 0/1/2` to `PerformSetup` or `SetupMonitor`, or `initialState: 0/1/2` to `QvInfoX_HoldSetup.Setup`. Creation arguments never overwrite an existing Controller. After adding/removing pens or changing the selection/version, rerun setup. Program assets and UI references are generated by the existing installer; use its Undo/Redo and Remove operations.

**Upgrade the Controller entry setting from the old bool (unrelated to Pickup version):** rerun シーンに追加・更新 (Auto Setup) once before editing the new entry mode, then save and rebuild. The serialized `initiallyEnabled` bool is retained but hidden for compatibility. Old `true` migrates once to mode 1 (ON); old `false` maps to mode 0 (Scene), preserving the previous no-write entry behavior. Setup consumes the legacy bool, so later author edits to any of the three modes survive repeated setup. Before that migration, runtime also honors old `true` as ON when `initialState` is 0; an explicit mode 1 or 2 takes precedence. This means setting mode 0 on an unmigrated old-true Controller alone cannot override the legacy flag: run setup first. Existing Editor bool arguments and `QvInfoXSetupOptions.InitiallyEnableHold` remain source-compatible: `true` requests mode 1 for creation when the new mode is 0; `false` uses Scene. New explicit mode 1 or 2 takes precedence over the bool. No automatic bool-to-int serialization conversion is assumed.

The feature assumes fixed scene references and an always-active Controller. In VR, initial ON/OFF applies Yes/No immediately only to unheld targets; Scene mode makes no writes until the first request. Wired inactive pens are included: an unheld Pickup can be set while inactive and retains that value on activation. Pending changes are checked from Update at 0.25-second intervals, without SDK delayed-event reservations. Early or stale `_ProcessPending` callbacks do nothing and cannot stop the Update scan. Tab changes and player/pen respawn retain the local request. Dynamically spawned pens and concurrent scripts writing AutoHold on the same pens are unsupported; Scene mode is not a continuous external-writer monitor. Disabling the Controller pauses pending work; re-enabling resumes it. Deleting it leaves the last locally applied values in place; there is no restoration lifecycle. Keep it active until pending requests finish. The 2026-11-04 real-device check covered VR ON/OFF switching, desktop always-hold behavior, and a build; the devices were not recorded. ClientSim is only a UI/value-check aid.

## Troubleshooting

- **Missing menu or compiler errors:** verify the package dependencies and wait for Unity to finish compiling before rerunning シーンに追加・更新 (Auto Setup).
- **Setup warning:** rerun **シーンに追加・更新 (Auto Setup)**. Check that the active scene has exactly one WorldState and that the generated Monitor and PlayerPool are enabled.
- **Pending totals after joining:** Master totals are shown only after a valid current-Master snapshot arrives. Use two clients to check late join and Master handoff.
- **Pen count mismatch:** A `masterPenStrokes` array length can differ from the scene pen count. Pen details remain pending until a matching report arrives.
- **Text or layout issue:** cycle all three languages and check long player names in a VRChat client. Editor validation alone cannot prove glyph rendering on Quest.
