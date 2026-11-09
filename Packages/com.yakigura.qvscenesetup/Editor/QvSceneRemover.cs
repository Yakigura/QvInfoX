using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yakigura.QvSceneSetup;

namespace Yakigura.QvSceneSetup.Editor
{
    public sealed class QvSceneRemovePlan
    {
        public readonly List<string> delete = new List<string>();
        public readonly List<string> restore = new List<string>();
        public readonly List<string> conflicts = new List<string>();
        public readonly List<string> warnings = new List<string>();

        public override string ToString() => "削除:\n" + string.Join("\n", delete)
            + "\n復元:\n" + string.Join("\n", restore)
            + "\n競合:\n" + string.Join("\n", conflicts)
            + "\n注意:\n" + string.Join("\n", warnings);
    }

    public static class QvSceneRemover
    {
        public static QvSceneRemovePlan DryRun(Scene scene, string featureId,
            Func<QvScenePropertyChange, bool> changedBeforeRemove = null)
        {
            var record = QvSceneRecorder.Find(scene, featureId);
            if (record == null) throw new InvalidOperationException(featureId + " の導入記録がありません。旧シーンは先に取り込みが必要です。");
            var plan = new QvSceneRemovePlan();
            foreach (var go in record.createdObjects.Where(o => o != null)) plan.delete.Add(go.name);
            foreach (var component in record.createdComponents.Where(o => o != null))
                if (!record.createdObjects.Contains(component.gameObject))
                    plan.delete.Add(component.gameObject.name + "/" + component.GetType().Name);
            var created = new HashSet<GameObject>(record.createdObjects.Where(o => o != null));
            var inventory = new HashSet<Component>(record.createdComponents.Where(c => c != null));
            var preserved = new HashSet<GameObject>();
            foreach (var go in created)
            {
                if (!record.createdComponentInventoryComplete)
                {
                    plan.warnings.Add("旧版の導入記録にはコンポーネント一覧がありません: " + go.name);
                    preserved.Add(go);
                    continue;
                }
                foreach (var component in go.GetComponents<Component>())
                    if (component != null && !inventory.Contains(component))
                    {
                        plan.warnings.Add("追加されたコンポーネントを保護します: " + go.name + "/" + component.GetType().Name);
                        preserved.Add(go);
                    }
                foreach (Transform child in go.transform)
                    if (!created.Contains(child.gameObject))
                    {
                        plan.warnings.Add("追加された子を保護します: " + go.name + "/" + child.name);
                        preserved.Add(go);
                    }
            }
            foreach (var go in preserved.ToArray())
                for (var parent = go.transform.parent; parent != null && created.Contains(parent.gameObject); parent = parent.parent)
                    preserved.Add(parent.gameObject);
            foreach (var go in preserved) plan.delete.Remove(go.name);
            foreach (var change in record.changes)
            {
                if (change.target == null) { plan.warnings.Add("変更対象が削除済み: " + change.path); continue; }
                if (change.removedDuringAdd)
                {
                    plan.restore.Add(change.target.name + "." + change.path + " (配列要素)");
                    continue;
                }
                var so = new SerializedObject(change.target);
                var property = so.FindProperty(change.path);
                if (property == null || !QvSceneSnapshot.QvSceneCell.TryRead(property, out var current))
                { plan.warnings.Add("値を読めません: " + change.target.name + "." + change.path); continue; }
                string label = change.target.name + "." + change.path;
                if (changedBeforeRemove != null && changedBeforeRemove(change)
                    || current.Same(QvSceneSnapshot.QvSceneCell.FromChange(change, true))) plan.restore.Add(label);
                else if (!current.Same(QvSceneSnapshot.QvSceneCell.FromChange(change, false))) plan.conflicts.Add(label);
            }
            plan.warnings.AddRange(record.irreversibleChanges);
            foreach (var deleted in record.deletedDuringAdd)
                plan.warnings.Add("追加時に削除された物（取り除いても復元できません）: " + deleted);
            if (record.networkObjects.Count > 0) plan.warnings.Add("NetworkID を " + record.networkObjects.Count + " 件解除");
            return plan;
        }

        public static QvSceneRemovePlan Remove(Scene scene, string featureId, bool force,
            Action beforeRemove = null, Func<QvScenePropertyChange, bool> changedBeforeRemove = null)
        {
            var record = QvSceneRecorder.Find(scene, featureId);
            var plan = DryRun(scene, featureId, changedBeforeRemove);
            if (plan.conflicts.Count > 0 && !force)
                foreach (var conflict in plan.conflicts)
                    plan.warnings.Add("導入後の変更を維持しました: " + conflict);
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(featureId + " Remove from Scene");
            try
            {
                beforeRemove?.Invoke();
                // Restore first, so references introduced by this installer do not protect its own objects.
                foreach (var change in record.changes.OrderBy(c => c.path.EndsWith(".Array.size", StringComparison.Ordinal) ? 0 : 1))
                {
                    if (change.target == null) continue;
                    var serialized = new SerializedObject(change.target);
                    var property = serialized.FindProperty(change.path);
                    if (property == null || !QvSceneSnapshot.QvSceneCell.TryRead(property, out var current)) continue;
                    var installed = QvSceneSnapshot.QvSceneCell.FromChange(change, true);
                    if (!force && !change.removedDuringAdd && !current.Same(installed)) continue;
                    Undo.RecordObject(change.target, featureId + " restore value");
                    if (PrefabUtility.IsPartOfPrefabInstance(change.target) && !change.hadPrefabOverride)
                        PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                    else
                    {
                        QvSceneSnapshot.QvSceneCell.FromChange(change, false)
                            .Write(property, change.before, change.beforeReference);
                        serialized.ApplyModifiedProperties();
                    }
                }
                var created = new HashSet<GameObject>(record.createdObjects.Where(o => o != null));
                var createdComponents = new HashSet<Component>(record.createdComponents.Where(o => o != null));
                var protectedObjects = FindSharedObjects(scene, created, createdComponents, record);
                foreach (var go in created)
                {
                    if (!record.createdComponentInventoryComplete
                        || go.GetComponents<Component>().Any(c => c != null && !createdComponents.Contains(c))
                        || go.transform.Cast<Transform>().Any(t => !created.Contains(t.gameObject)))
                        protectedObjects.Add(go);
                }
                foreach (var go in protectedObjects)
                    plan.warnings.Add("共有参照があるため残しました: " + go.name);
                // A protected descendant requires its created ancestors too.
                foreach (var go in protectedObjects.ToArray())
                    for (var parent = go.transform.parent; parent != null && created.Contains(parent.gameObject); parent = parent.parent)
                        protectedObjects.Add(parent.gameObject);
                if (featureId == "QvInfoX" && protectedObjects.Any(o => o.name.StartsWith("QvInkPreview", StringComparison.Ordinal)))
                    foreach (var go in created.Where(o => o != null && o.name == "QvInkPreview_Manager"))
                        protectedObjects.Add(go);
                if (featureId == "QvInfoX")
                {
                    bool hasOtherUi = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
                        .Any(canvas => canvas != null && !created.Contains(canvas.gameObject)
                            && canvas.gameObject.name != "Join Mode Panel");
                    if (hasOtherUi)
                        foreach (var go in created.Where(o => o != null && o.GetComponents<Component>()
                            .Any(c => c != null && c.GetType().Name == "EventSystem")))
                        {
                            protectedObjects.Add(go);
                            plan.warnings.Add("ほかの UI があるため EventSystem を残しました");
                        }
                }
                var descriptor = FindByTypeName(scene, "VRCSceneDescriptor");
                var collection = (descriptor?.GetType().GetField("NetworkIDCollection")?.GetValue(descriptor)
                    ?? descriptor?.GetType().GetProperty("NetworkIDCollection")?.GetValue(descriptor))
                    as System.Collections.IList;
                if (collection != null)
                {
                    Undo.RecordObject(descriptor, featureId + " NetworkID");
                    for (int i = collection.Count - 1; i >= 0; --i)
                    {
                        var pair = collection[i];
                        var target = pair.GetType().GetField("gameObject")?.GetValue(pair) as GameObject;
                        if (record.networkObjects.Contains(target) && !protectedObjects.Contains(target))
                            collection.RemoveAt(i);
                    }
                }
                foreach (var protectedGo in protectedObjects.ToArray())
                    foreach (var child in protectedGo.GetComponentsInChildren<Transform>(true))
                        if (created.Contains(child.gameObject)) protectedObjects.Add(child.gameObject);
                foreach (var component in record.createdComponents.Where(c => c != null).ToArray())
                    if (!created.Contains(component.gameObject) && !protectedObjects.Contains(component.gameObject))
                        Undo.DestroyObjectImmediate(component);
                foreach (var go in created.OrderByDescending(Depth))
                    if (go != null && !protectedObjects.Contains(go)) Undo.DestroyObjectImmediate(go);
                Undo.DestroyObjectImmediate(record.gameObject);
                QvSceneRecorder.CopyProxies(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
                return plan;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static HashSet<GameObject> FindSharedObjects(Scene scene, HashSet<GameObject> created,
            HashSet<Component> createdComponents, QvSceneInstallRecord currentRecord)
        {
            var protectedObjects = new HashSet<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component is QvSceneInstallRecord
                        || createdComponents.Contains(component)) continue;
                    var serialized = new SerializedObject(component);
                    var iterator = serialized.GetIterator();
                    if (!iterator.NextVisible(true)) continue;
                    do
                    {
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference
                            || component.GetType().Name == "VRCSceneDescriptor"
                                && (iterator.propertyPath.StartsWith("NetworkIDCollection", StringComparison.Ordinal)
                                    || iterator.propertyPath.StartsWith("NetworkIDs", StringComparison.Ordinal))) continue;
                        var referenced = iterator.objectReferenceValue;
                        var go = referenced as GameObject;
                        if (go == null && referenced is Component referencedComponent) go = referencedComponent.gameObject;
                        if (go != null && created.Contains(go) && protectedObjects.Add(go))
                            Debug.LogWarning("共有参照を保護: " + component.name + "." + iterator.propertyPath + " → " + go.name);
                    } while (iterator.NextVisible(true));
                    if (component.GetType().Name == "UdonBehaviour")
                        foreach (var referenced in UdonPublicReferences(component))
                        {
                            var go = referenced as GameObject;
                            if (go == null && referenced is Component referencedComponent) go = referencedComponent.gameObject;
                            if (go != null && created.Contains(go) && protectedObjects.Add(go))
                                Debug.LogWarning("Udon 共有参照を保護: " + component.name + " → " + go.name);
                        }
                }
            return protectedObjects;
        }

        private static IEnumerable<UnityEngine.Object> UdonPublicReferences(Component udon)
        {
            var table = udon.GetType().GetField("publicVariables")?.GetValue(udon);
            if (table == null) yield break;
            var symbols = table.GetType().GetProperty("VariableSymbols")?.GetValue(table)
                as System.Collections.IEnumerable;
            var read = table.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == "TryGetVariableValue" && !method.IsGenericMethod
                    && method.GetParameters().Length == 2
                    && method.GetParameters()[0].ParameterType == typeof(string));
            if (symbols == null || read == null) yield break;
            foreach (var symbol in symbols)
            {
                var args = new object[] { symbol, null };
                if (!(bool)read.Invoke(table, args)) continue;
                if (args[1] is UnityEngine.Object single) yield return single;
                else if (args[1] is System.Collections.IEnumerable collection && !(args[1] is string))
                    foreach (var item in collection)
                        if (item is UnityEngine.Object referenced) yield return referenced;
            }
        }

        private static int Depth(GameObject go)
        {
            int depth = 0;
            for (var parent = go.transform.parent; parent != null; parent = parent.parent) depth++;
            return depth;
        }

        private static Component FindByTypeName(Scene scene, string typeName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component != null && component.GetType().Name == typeName) return component;
            }
            return null;
        }
    }
}
