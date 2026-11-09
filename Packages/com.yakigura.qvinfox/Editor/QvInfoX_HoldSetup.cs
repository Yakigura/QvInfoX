using System.Collections.Generic;
using QvPen.UdonScript;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace QvInfoX.Editor
{
    /// <summary>Wires QvHold targets without changing scene Pickup settings.</summary>
    public static class QvInfoX_HoldSetup
    {
        public const string ControllerName = "HoldController";

        public static QvInfoX_HoldController Setup(Transform monitor, out int skippedCount,
            bool initiallyEnabled = false, int initialState = 0)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                var controller = SetupPrepared(monitor, out skippedCount, initiallyEnabled, initialState);
                Undo.CollapseUndoOperations(group);
                return controller;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        internal static QvInfoX_HoldController SetupPrepared(Transform monitor, out int skippedCount,
            bool initiallyEnabled, int initialState = 0)
        {
            var child = monitor.Find(ControllerName);
            if (child == null)
            {
                var go = new GameObject(ControllerName);
                Undo.RegisterCreatedObjectUndo(go, "Create QvInfoX Hold Controller");
                go.transform.SetParent(monitor, false);
                child = go.transform;
            }
            var isNewController = child.GetComponent<QvInfoX_HoldController>() == null;
            var controller = QvInfoX_AutoSetup.GetOrAddUdonSharpComponent<QvInfoX_HoldController>(child.gameObject);
            Undo.RecordObject(controller, "Configure QvInfoX Hold Controller");
            child.gameObject.SetActive(true);
            controller.enabled = true;
            CollectTargets(monitor.gameObject.scene, controller.selectedPensOnly, controller.selectedPens,
                out QvPen_Pen[] pens, out VRCPickup[] pickups, out skippedCount);
            if (pens.Length == 0)
                Debug.LogWarning("[QvHold] 対象 0 本のため「対応するペンなし」になり、トグルは操作できません。"
                    + $" スキップ {skippedCount} 本。対象候補がないか、全候補が非対応/無効です。原因の詳細は上のペン別警告を確認してください。"
                    + " QvPen_Pen と同じオブジェクトに VRCPickup があるか、対象選択とアクティブなワールドシーンを確認し、"
                    + "Pickup のバージョンを確認して自動セットアップを再実行してください。", monitor);
            var so = new SerializedObject(controller);
            // Creation-only default: a subsequent setup must preserve the author's Inspector value.
            var entryProperty = so.FindProperty("initialState");
            var legacyProperty = so.FindProperty("initiallyEnabled");
            if (isNewController)
                entryProperty.intValue = ResolveEntry(initialState, initiallyEnabled);
            else if (entryProperty.intValue == 0 && legacyProperty.boolValue)
                entryProperty.intValue = 1; // One-time migration preserves old entry-ON intent.
            legacyProperty.boolValue = false;
            var penProperty = so.FindProperty("targetPens");
            var pickupProperty = so.FindProperty("targetPickups");
            penProperty.arraySize = pens.Length;
            pickupProperty.arraySize = pickups.Length;
            for (var i = 0; i < pens.Length; i++)
            {
                penProperty.GetArrayElementAtIndex(i).objectReferenceValue = pens[i];
                pickupProperty.GetArrayElementAtIndex(i).objectReferenceValue = pickups[i];
            }
            so.FindProperty("statusReceiver").objectReferenceValue = monitor.GetComponent<QvInfoX_Manager>();
            int yesCount = 0;
            foreach (var pickup in pickups)
                if (pickup.AutoHold == VRC_Pickup.AutoHoldMode.Yes) yesCount++;
            int entry = entryProperty.intValue;
            // Preview only: setup never applies the requested runtime values to Pickups.
            so.FindProperty("requestedEnabled").boolValue = pens.Length > 0
                && (entry == 1 || (entry == 0 && yesCount == pens.Length));
            so.FindProperty("eligibleCount").intValue = pens.Length;
            so.FindProperty("appliedCount").intValue = yesCount;
            so.FindProperty("pendingCount").intValue = 0;
            so.FindProperty("mixedState").boolValue = entry == 0 && yesCount > 0 && yesCount < pens.Length;
            so.ApplyModifiedProperties();
            return controller;
        }

        private static int ResolveEntry(int configured, bool legacyEnabled)
        {
            if (configured == 1 || configured == 2) return configured;
            return legacyEnabled ? 1 : 0;
        }

        public static void CollectTargets(Scene scene, bool selectedOnly, QvPen_Pen[] selected,
            out QvPen_Pen[] pens, out VRCPickup[] pickups, out int skippedCount)
        {
            var foundPens = new List<QvPen_Pen>();
            var foundPickups = new List<VRCPickup>();
            var seenPens = new HashSet<QvPen_Pen>();
            var seenPickups = new HashSet<VRCPickup>();
            skippedCount = 0;
            foreach (var pen in Candidates(scene, selectedOnly, selected))
            {
                if (pen != null && !EditorUtility.IsPersistent(pen) && pen.gameObject.scene == scene
                    && !seenPens.Add(pen)) continue;
                var reason = IneligibleReason(scene, pen, out var pickup);
                if (reason == null && pickup.version != VRCPickup.Version.Version_1_0
                    && pickup.version != VRCPickup.Version.Version_1_1)
                    reason = $"未対応の VRCPickup バージョン ({pickup.version})。";
                if (reason != null)
                {
                    skippedCount++;
                    Debug.LogWarning($"[QvHold] スキップ: {PenPath(pen)} — {reason}", pen);
                    continue;
                }
                if (!seenPickups.Add(pickup)) continue;
                foundPens.Add(pen);
                foundPickups.Add(pickup);
            }
            pens = foundPens.ToArray();
            pickups = foundPickups.ToArray();
        }

        internal static List<QvPen_Pen> Candidates(Scene scene, bool selectedOnly, QvPen_Pen[] selected)
        {
            var candidates = new List<QvPen_Pen>();
            if (selectedOnly)
            {
                if (selected != null) candidates.AddRange(selected);
            }
            else if (scene.IsValid() && scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects())
                    candidates.AddRange(root.GetComponentsInChildren<QvPen_Pen>(true));
            return candidates;
        }

        internal static string SceneReason(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "シーン外、またはシーンがロードされていません。";
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene == scene) return "Prefab Stage はワールドシーンではありません。";
            if (EditorSceneManager.IsPreviewScene(scene)) return "プレビューシーンは対象外です。";
            if (scene != SceneManager.GetActiveScene()) return "アクティブなワールドシーンの外です。";
            return null;
        }

        internal static string IneligibleReason(Scene scene, QvPen_Pen pen, out VRCPickup pickup)
        {
            pickup = null;
            if (pen == null) return "参照が null または削除済みです。";
            if (EditorUtility.IsPersistent(pen) || PrefabUtility.IsPartOfPrefabAsset(pen))
                return "Prefab アセットは変更・配線しません。シーンのインスタンスを指定してください。";
            var sceneReason = SceneReason(scene);
            if (sceneReason != null) return sceneReason;
            if (pen.gameObject.scene != scene) return "対象シーン外のペンです。";
            pickup = pen.GetComponent<VRCPickup>();
            if (pickup == null)
                return pen.GetComponentInChildren<VRCPickup>(true) != null
                    ? "pickup が別オブジェクト (子) にあります。同じ GameObject の VRCPickup が必要です。"
                    : "同じ GameObject に VRCPickup がありません。";
            if (pickup.gameObject != pen.gameObject) return "pickup が別オブジェクトにあります。";
            return null;
        }

        internal static string PenPath(QvPen_Pen pen)
        {
            if (pen == null) return "<null / 削除済み>";
            var names = new Stack<string>();
            for (var current = pen.transform; current != null; current = current.parent) names.Push(current.name);
            var scene = pen.gameObject.scene;
            var location = EditorUtility.IsPersistent(pen) ? AssetDatabase.GetAssetPath(pen)
                : string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
            return location + ":" + string.Join("/", names);
        }
    }
}
