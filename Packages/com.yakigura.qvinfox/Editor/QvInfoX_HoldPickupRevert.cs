using System;
using System.Linq;
using QvPen.UdonScript;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace QvInfoX.Editor
{
    /// <summary>Reverts QvPen scene pickups without modifying their prefab assets.</summary>
    public static class QvInfoX_HoldPickupRevert
    {
        public struct Result
        {
            public int Reverted;
            public int AlreadyV10;
            // Pickups without a pen and pens without a same-object pickup.
            public int Skipped;
        }

        #if QVINFOX_DIAG
        [MenuItem("Tools/Yakigura/QvInfoX/QvHold/ペンの Pickup を v1.0 に戻す（開いているシーン）")]
        #endif
        public static void RevertOpenScenes()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("QvHold Pickup v1.0 restore");
            var total = new Result();
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded) Add(ref total, RevertSceneCore(scene));
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            LogResult("読み込み済みシーン合計", total);
        }

        #if QVINFOX_DIAG
        [MenuItem("Tools/Yakigura/QvInfoX/QvHold/ペンの Pickup を v1.0 に戻す（Assets/Scenes の全シーン）")]
        #endif
        public static void RevertAllScenes()
        {
            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var total = new Result();
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                Debug.Log($"[QvHold] シーン {i + 1}/{paths.Length}: {path}");
                var existing = SceneManager.GetSceneByPath(path);
                bool wasInHierarchy = existing.IsValid();
                bool wasLoaded = wasInHierarchy && existing.isLoaded;
                var scene = wasLoaded ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var result = RevertScene(scene);
                    Add(ref total, result);
                    if (result.Reverted > 0 && !EditorSceneManager.SaveScene(scene))
                        throw new InvalidOperationException("シーンを保存できません: " + path);
                }
                finally
                {
                    if (!wasLoaded) EditorSceneManager.CloseScene(scene, !wasInHierarchy);
                }
            }
            LogResult("Assets/Scenes 合計", total);
        }

        public static Result RevertScene(Scene scene)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("QvHold Pickup v1.0 restore");
            try { return RevertSceneCore(scene); }
            finally { Undo.CollapseUndoOperations(group); }
        }

        private static Result RevertSceneCore(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("読み込み済みのシーンを指定してください。", nameof(scene));
            var result = new Result();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var pickup in root.GetComponentsInChildren<VRCPickup>(true))
                    if (pickup.GetComponent<QvPen_Pen>() == null) result.Skipped++;
                foreach (var pen in root.GetComponentsInChildren<QvPen_Pen>(true))
                {
                    var pickup = pen.GetComponent<VRCPickup>();
                    if (pickup == null)
                    {
                        result.Skipped++;
                        Debug.Log($"[QvHold] スキップ: {QvInfoX_HoldSetup.PenPath(pen)} — 同じ GameObject に Pickup がありません。");
                        continue;
                    }
                    var beforeVersion = pickup.version;
                    var beforeHold = pickup.AutoHold;
                    if (beforeVersion == VRCPickup.Version.Version_1_0)
                    {
                        result.AlreadyV10++;
                    }
                    else
                    {
                        Undo.RegisterCompleteObjectUndo(pickup, "QvHold Pickup v1.0 restore");
                        // The PrefabInstance owns override metadata, separate from the component values.
                        var handle = PrefabUtility.GetPrefabInstanceHandle(pickup);
                        if (handle != null) Undo.RegisterCompleteObjectUndo(handle, "QvHold Pickup v1.0 restore");
                        var source = PrefabUtility.GetCorrespondingObjectFromSource(pickup);
                        var serialized = new SerializedObject(pickup);
                        if (source != null)
                        {
                            RevertOverride(serialized, nameof(VRCPickup.version));
                            RevertOverride(serialized, nameof(VRCPickup.AutoHold));
                            RevertOverride(serialized, nameof(VRCPickup.orientation));
                        }
                        // A prefab source can itself be v1.1, or the component can be an added override.
                        if (pickup.version != VRCPickup.Version.Version_1_0)
                        {
                            serialized.Update();
                            serialized.FindProperty(nameof(VRCPickup.version)).intValue = (int)VRCPickup.Version.Version_1_0;
                            var autoHold = source != null ? source.AutoHold
                                : pickup.AutoHold == VRC_Pickup.AutoHoldMode.Yes
                                    ? VRC_Pickup.AutoHoldMode.Sometimes : pickup.AutoHold;
                            serialized.FindProperty(nameof(VRCPickup.AutoHold)).intValue = (int)autoHold;
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                            PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
                        }
                        EditorSceneManager.MarkSceneDirty(scene);
                        result.Reverted++;
                    }
                    Debug.Log($"[QvHold] {QvInfoX_HoldSetup.PenPath(pen)} — version {beforeVersion} → {pickup.version}, AutoHold {beforeHold} → {pickup.AutoHold}");
                }
            }
            LogResult(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path, result);
            return result;
        }

        private static void RevertOverride(SerializedObject serialized, string name)
        {
            serialized.Update();
            var property = serialized.FindProperty(name);
            if (property.prefabOverride)
                PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
        }

        private static void Add(ref Result total, Result result)
        {
            total.Reverted += result.Reverted;
            total.AlreadyV10 += result.AlreadyV10;
            total.Skipped += result.Skipped;
        }

        private static void LogResult(string label, Result result)
        {
            Debug.Log($"[QvHold] {label}: 戻した数 {result.Reverted}、すでに v1.0 {result.AlreadyV10}、スキップ（対象外）{result.Skipped}");
        }
    }
}
