using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using QvPen.UdonScript;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using Yakigura.QvSceneSetup.Editor;

namespace QvInfoX.Editor
{
    public enum QvInfoXMonitorPlacement { KeepCurrent, SpawnRelative, Absolute }

    [Serializable]
    public sealed class QvInfoXSetupOptions
    {
        public QvInfoXMonitorPlacement Placement = QvInfoXMonitorPlacement.KeepCurrent;
        public Vector3 Offset = new Vector3(0f, 1.4f, 2f);
        public Vector3 AbsolutePosition;
        public float AbsoluteYaw;
        public bool OverrideExistingPlacement;
        // Creation only. Existing HoldController Inspector settings always take precedence.
        public int HoldInitialState; // 0: scene (default), 1: ON, 2: OFF.
        // Legacy source compatibility: true maps to ON when HoldInitialState is 0.
        public bool InitiallyEnableHold;
    }

    public static class QvInfoX_SceneInstaller
    {
        public const string FeatureId = "QvInfoX";
        public const string InfrastructureParentName = "QvInfoX";
        private static readonly string[] InfrastructureObjectNames =
        {
            QvInfoX_AutoSetup.PLAYER_POOL_ROOT_NAME, QvInfoX_AutoSetup.WORLD_STATE_ROOT_NAME,
            QvInfoX_AutoSetup.MASTER_TOTALS_ROOT_NAME, QvInfoX_AutoSetup.MONITOR_ROOT_NAME
        };

        public static Transform FindInfrastructureParent(Scene scene)
        {
            var matches = FindAll<Transform>(scene).Where(item => item.name == InfrastructureParentName).ToArray();
            if (matches.Length > 1) Debug.LogWarning("Multiple 'QvInfoX' parents found; using the first");
            if (matches.Length == 0) return null;
            return matches[0];
        }

        public static void ValidateInfrastructureLayout(Scene scene)
        {
            FindInfrastructureParent(scene);
            foreach (var name in InfrastructureObjectNames) QvInfoX_AutoSetup.FindSceneRoot(scene, name);
        }

        private static Transform GetOrCreateInfrastructureParent(Scene scene)
        {
            var parent = FindInfrastructureParent(scene);
            if (parent != null) return parent;
            var go = new GameObject(InfrastructureParentName);
            SceneManager.MoveGameObjectToScene(go, scene);
            // Register while empty, before existing children are moved into it.
            Undo.RegisterCreatedObjectUndo(go, "QvInfoX infrastructure parent");
            return go.transform;
        }

        private static bool OwnsInfrastructureParent(Scene scene, Transform parent)
        {
            var record = QvSceneRecorder.Find(scene, FeatureId);
            return record == null || parent != null && record.createdObjects.Contains(parent.gameObject)
                && record.createdComponents.Contains(parent);
        }

        private static void TrackInfrastructureParent(Scene scene, Transform parent)
        {
            var record = QvSceneRecorder.Find(scene, FeatureId);
            if (record == null || parent == null || OwnsInfrastructureParent(scene, parent)) return;
            Undo.RecordObject(record, "QvInfoX infrastructure parent record");
            if (!record.createdObjects.Contains(parent.gameObject)) record.createdObjects.Add(parent.gameObject);
            if (!record.createdComponents.Contains(parent)) record.createdComponents.Add(parent);
            EditorUtility.SetDirty(record);
        }

        private static void GroupInfrastructureObjects(Scene scene)
        {
            ValidateInfrastructureLayout(scene);
            var parent = GetOrCreateInfrastructureParent(scene);
            foreach (var name in InfrastructureObjectNames)
            {
                var go = QvInfoX_AutoSetup.FindSceneRoot(scene, name);
                if (go != null && go.transform != parent && go.transform.parent != parent && !parent.IsChildOf(go.transform))
                    ReparentInfrastructureObject(go.transform, parent);
            }
        }

        private static void ReparentInfrastructureObject(Transform child, Transform parent)
        {
            var manager = child.GetComponent<QvInfoX_Manager>();
            var oldParent = child.parent;
            var savedPosition = manager != null && manager.initialPoseCaptured
                ? (oldParent != null ? oldParent.TransformPoint(manager.initialLocalPosition) : manager.initialLocalPosition)
                : Vector3.zero;
            var savedRotation = manager != null && manager.initialPoseCaptured
                ? (oldParent != null ? oldParent.rotation * manager.initialLocalRotation : manager.initialLocalRotation)
                : Quaternion.identity;
            Undo.SetTransformParent(child, parent, "Group QvInfoX infrastructure");
            if (manager == null || !manager.initialPoseCaptured) return;
            var localPosition = parent.InverseTransformPoint(savedPosition);
            var localRotation = Quaternion.Inverse(parent.rotation) * savedRotation;
            if ((localPosition - manager.initialLocalPosition).sqrMagnitude < 1e-10f
                && Quaternion.Angle(localRotation, manager.initialLocalRotation) < 0.0001f) return;
            Undo.RecordObject(manager, "Preserve QvInfoX reset pose");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backing != null) Undo.RecordObject(backing, "Preserve QvInfoX reset pose");
            manager.initialLocalPosition = localPosition;
            manager.initialLocalRotation = localRotation;
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
        }

        // Hierarchy migration preserves the monitor's saved reset pose across reparenting.
        public static bool EnsureInfrastructureGrouping(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || Application.isPlaying)
                throw new InvalidOperationException("A loaded edit-mode scene is required for infrastructure grouping");

            ValidateInfrastructureLayout(scene);
            var issues = QvSceneRecorder.Find(scene, FeatureId) == null
                ? new List<string>() : QvSceneRecorder.Integrity(scene, FeatureId);
            if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues));
            var objects = InfrastructureObjectNames.Select(name => QvInfoX_AutoSetup.FindSceneRoot(scene, name))
                .Where(go => go != null).ToArray();
            var parent = FindInfrastructureParent(scene);
            if (parent != null && objects.All(go => go.transform == parent || go.transform.parent == parent || parent.IsChildOf(go.transform))
                && OwnsInfrastructureParent(scene, parent)) return false;
            if (objects.Length == 0 && parent == null) return false;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                parent = GetOrCreateInfrastructureParent(scene);
                foreach (var go in objects)
                    if (go.transform != parent && go.transform.parent != parent && !parent.IsChildOf(go.transform))
                        ReparentInfrastructureObject(go.transform, parent);
                TrackInfrastructureParent(scene, parent);
                Undo.CollapseUndoOperations(group);
                return true;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        public static void Add(Scene scene, QvInfoXSetupOptions options = null)
        {
            ValidateInfrastructureLayout(scene);
            bool explicitOptions = options != null;
            options = options ?? new QvInfoXSetupOptions();
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("保存済みのロードされたシーンが必要です");
            var descriptor = Find<VRCSceneDescriptor>(scene);

            var managers = FindAll<QvPen_PenManager>(scene);
            if (managers.Count == 0) throw new InvalidOperationException("QvPen PenManager が必要です");
            foreach (var manager in managers)
            {
                var pen = manager.GetComponentInChildren<QvPen_Pen>(true);
                if (pen == null) continue;
                var syncer = pen.GetProgramVariable("syncer") as QvPen_LateSync;
                if (syncer == null || syncer.InkPoolSynced == null || syncer.InkPoolNotSynced == null)
                    throw new InvalidOperationException("QvPen sync pools are not wired: " + manager.name);
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            var previous = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                EnsureInfrastructureGrouping(scene);
                GetOrCreateInfrastructureParent(scene);
                TrackInfrastructureParent(scene, FindInfrastructureParent(scene));
                // Keep FastSync parent ownership outside this installer's snapshot too.
                InvokeFastSync(scene, "EnsureInfrastructureGrouping");
                QvInfoX_AutoSetup.EnsureAllProgramAssets();
                if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                    throw new InvalidOperationException("UdonSharp compile errors");
                var hadMonitor = Find<QvInfoX_Manager>(scene) != null;
                var previousRecord = QvSceneRecorder.Find(scene, FeatureId);
                var optionsJson = explicitOptions || previousRecord == null
                    ? JsonUtility.ToJson(options) : previousRecord.optionsJson;
                QvSceneRecorder.Install(scene, FeatureId, optionsJson, () =>
                {
                    QvInfoX_AutoSetup.PerformSetupPrepared(options.InitiallyEnableHold, options.HoldInitialState);
                    var monitor = Find<QvInfoX_Manager>(scene);
                    if (monitor != null && options.Placement != QvInfoXMonitorPlacement.KeepCurrent
                        && (!hadMonitor || options.OverrideExistingPlacement))
                    {
                        if (options.Placement == QvInfoXMonitorPlacement.SpawnRelative)
                        {
                            var spawn = descriptor != null && descriptor.spawns != null && descriptor.spawns.Length > 0
                                ? descriptor.spawns[0] : null;
                            var position = spawn != null ? spawn.position : Vector3.zero;
                            var rotation = spawn != null ? spawn.rotation : Quaternion.identity;
                            monitor.transform.position = position + rotation * Vector3.right * options.Offset.x
                                + Vector3.up * options.Offset.y + rotation * Vector3.forward * options.Offset.z;
                            monitor.transform.rotation = rotation;
                        }
                        else
                        {
                            monitor.transform.position = options.AbsolutePosition;
                            monitor.transform.rotation = Quaternion.Euler(0f, options.AbsoluteYaw, 0f);
                        }
                        monitor.initialLocalPosition = monitor.transform.localPosition;
                        monitor.initialLocalRotation = monitor.transform.localRotation;
                        monitor.initialPoseCaptured = true;
                    }
                    GroupInfrastructureObjects(scene);
                    RefreshFastSync(scene);
                    QvSceneRecorder.CopyProxies(scene);
                    var issues = QvInfoX_AutoSetup.CollectHealthIssues();
                    foreach (var issue in issues) Debug.LogWarning("QvInfoX setup: " + issue);
                });
                TrackInfrastructureParent(scene, FindInfrastructureParent(scene));
                Undo.CollapseUndoOperations(group);
            }
            catch (OperationCanceledException)
            {
                Undo.RevertAllDownToGroup(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        public static QvSceneRemovePlan Remove(Scene scene, bool force = false)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(FeatureId + " Remove from Scene");
            try
            {
                var record = QvSceneRecorder.Find(scene, FeatureId);
                if (record != null)
                {
                    // The remover destroys children before their parents. Snapshot all
                    // owned component state while cross-references are still alive:
                    // otherwise the later Manager/record snapshots already contain
                    // destroyed HoldController/UI references. Include backing Udon
                    // components as well as proxies, and restore these states last.
                    var targets = record.createdComponents.Where(component => component != null)
                        .Cast<UnityEngine.Object>().Concat(new UnityEngine.Object[] { record })
                        .Distinct().ToArray();
                    Undo.RegisterCompleteObjectUndo(targets, FeatureId + " references before Remove");
                }
                var result = QvSceneRemover.Remove(scene, FeatureId, force,
                    () => DetachFastSyncObserver(scene), IsDetachedObserverValue);
                RefreshFastSync(scene);
                Undo.CollapseUndoOperations(group);
                return result;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        public static List<string> Validate(Scene scene)
        {
            var previous = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                return QvInfoX_AutoSetup.CollectHealthIssues();
            }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        public static QvSceneRemovePlan DryRun(Scene scene) =>
            QvSceneRemover.DryRun(scene, FeatureId, IsDetachedObserverValue);

        private static bool IsDetachedObserverValue(Yakigura.QvSceneSetup.QvScenePropertyChange change)
        {
            if (!(change.target is Component component)
                || component.GetType().Name != "QvFastSyncUdonProgressObserver") return false;
            return change.path == "worldState" || change.path == "playerSlots.Array.size"
                || change.path.StartsWith("playerSlots.Array.data[", StringComparison.Ordinal);
        }

        public static void ImportExisting(Scene scene)
        {
            ValidateInfrastructureLayout(scene);
            QvSceneRecorder.ImportExisting(scene, FeatureId, go =>
                go.name == InfrastructureParentName || go.name.StartsWith("QvInfoX_", StringComparison.Ordinal)
                || go.name == "QvInkPreview" || go.name == "QvInkPreview_Manager");
            EnsureInfrastructureGrouping(scene);
        }

        private static void RefreshFastSync(Scene scene) => InvokeFastSync(scene, "Refresh");

        private static void InvokeFastSync(Scene scene, string methodName)
        {
            var type = Type.GetType("Yakigura.QvFastSync.Editor.Setup.QvFastSyncSceneInstaller, Yakigura.QvFastSync.Setup.Editor");
            var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null) return;
            var panel = FindByTypeName(scene, "QvFastSyncUdonJoinPanel");
            if (panel != null) method.Invoke(null, new object[] { scene });
        }

        private static void DetachFastSyncObserver(Scene scene)
        {
            var observer = FindByTypeName(scene, "QvFastSyncUdonProgressObserver");
            if (observer == null) return;
            Undo.RecordObject(observer, "Detach QvInfoX from FastSync");
            var type = observer.GetType();
            type.GetField("worldState")?.SetValue(observer, null);
            var slots = type.GetField("playerSlots");
            if (slots != null) slots.SetValue(observer, Array.CreateInstance(slots.FieldType.GetElementType(), 0));
            if (observer is UdonSharpBehaviour proxy) UdonSharpEditorUtility.CopyProxyToUdon(proxy);
        }

        private static Component FindByTypeName(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component != null && component.GetType().Name == name) return component;
            return null;
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        private static List<T> FindAll<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }
    }
}
