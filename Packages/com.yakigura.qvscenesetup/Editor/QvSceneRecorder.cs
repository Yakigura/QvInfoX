using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yakigura.QvSceneSetup;

namespace Yakigura.QvSceneSetup.Editor
{
    public sealed class QvSceneSnapshot
    {
        internal readonly HashSet<GameObject> objects = new HashSet<GameObject>();
        internal readonly Dictionary<GameObject, string> objectPaths = new Dictionary<GameObject, string>();
        internal readonly HashSet<Component> components = new HashSet<Component>();
        internal readonly Dictionary<UnityEngine.Object, Dictionary<string, QvSceneCell>> values =
            new Dictionary<UnityEngine.Object, Dictionary<string, QvSceneCell>>();
        internal readonly HashSet<GameObject> networkObjects = new HashSet<GameObject>();

        public static QvSceneSnapshot Capture(Scene scene)
        {
            var result = new QvSceneSnapshot();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = transform.gameObject;
                    result.objects.Add(go);
                    result.objectPaths[go] = PathOf(transform);
                    result.values[go] = Read(go);
                    foreach (var component in go.GetComponents<Component>())
                    {
                        if (component == null) continue;
                        result.components.Add(component);
                        result.values[component] = Read(component);
                        if (component.GetType().Name == "VRCSceneDescriptor")
                        {
                            var collection = (component.GetType().GetField("NetworkIDCollection")?.GetValue(component)
                                ?? component.GetType().GetProperty("NetworkIDCollection")?.GetValue(component))
                                as System.Collections.IEnumerable;
                            if (collection != null)
                                foreach (var pair in collection)
                                {
                                    var target = pair.GetType().GetField("gameObject")?.GetValue(pair) as GameObject;
                                    if (target != null) result.networkObjects.Add(target);
                                }
                        }
                    }
                }
            return result;
        }

        private static string PathOf(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names);
        }

        private static Dictionary<string, QvSceneCell> Read(UnityEngine.Object target)
        {
            var result = new Dictionary<string, QvSceneCell>();
            var serialized = new SerializedObject(target);
            if (target is GameObject)
            {
                var active = serialized.FindProperty("m_IsActive");
                if (active != null && QvSceneCell.TryRead(active, out var activeCell))
                    result["m_IsActive"] = activeCell;
            }
            var iterator = serialized.GetIterator();
            if (!iterator.NextVisible(true)) return result;
            do
            {
                var path = iterator.propertyPath;
                if (path == "m_Script" || path.StartsWith("m_Component", StringComparison.Ordinal)
                    || path.StartsWith("m_Children", StringComparison.Ordinal)
                    || path.StartsWith("DynamicMaterials", StringComparison.Ordinal)
                    || path.StartsWith("NetworkIDCollection", StringComparison.Ordinal)
                    || path.StartsWith("NetworkIDs", StringComparison.Ordinal)
                    || path.StartsWith("m_EditorClassIdentifier", StringComparison.Ordinal)
                    || path.StartsWith("serializedPublicVariables", StringComparison.Ordinal)
                    || path.StartsWith("publicVariables", StringComparison.Ordinal)) continue;
                if (QvSceneCell.TryRead(iterator, out var cell)) result[path] = cell;
            } while (iterator.NextVisible(true));
            return result;
        }

        internal static void Restore(QvSceneSnapshot before)
        {
            foreach (var pair in before.values)
            {
                if (pair.Key == null) continue;
                var serialized = new SerializedObject(pair.Key);
                foreach (var entry in pair.Value.OrderBy(p => p.Key.EndsWith(".Array.size", StringComparison.Ordinal) ? 0 : 1))
                {
                    var property = serialized.FindProperty(entry.Key);
                    if (property == null || !property.editable
                        || !QvSceneCell.TryRead(property, out var current) || entry.Value.Same(current)) continue;
                    if (PrefabUtility.IsPartOfPrefabInstance(pair.Key) && !entry.Value.prefabOverride)
                        PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                    else
                    {
                        entry.Value.Write(property, entry.Value.value, entry.Value.reference);
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
        }

        internal sealed class QvSceneCell
        {
            internal SerializedPropertyType type;
            internal string value;
            internal UnityEngine.Object reference;
            internal bool prefabOverride;

            internal static bool TryRead(SerializedProperty p, out QvSceneCell cell)
            {
                cell = new QvSceneCell { type = p.propertyType, prefabOverride = p.prefabOverride };
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer: cell.value = p.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: cell.value = p.boolValue ? "1" : "0"; break;
                    case SerializedPropertyType.Float: cell.value = p.doubleValue.ToString("R", CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.String: cell.value = p.stringValue; break;
                    case SerializedPropertyType.Enum: cell.value = p.enumValueIndex.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.ObjectReference: cell.reference = p.objectReferenceValue; break;
                    case SerializedPropertyType.Vector2: cell.value = JsonUtility.ToJson(new Vector2Value { value = p.vector2Value }); break;
                    case SerializedPropertyType.Vector3: cell.value = JsonUtility.ToJson(new Vector3Value { value = p.vector3Value }); break;
                    case SerializedPropertyType.Vector4: cell.value = JsonUtility.ToJson(new Vector4Value { value = p.vector4Value }); break;
                    case SerializedPropertyType.Quaternion: cell.value = JsonUtility.ToJson(new QuaternionValue { value = p.quaternionValue }); break;
                    case SerializedPropertyType.Color: cell.value = JsonUtility.ToJson(new ColorValue { value = p.colorValue }); break;
                    case SerializedPropertyType.Generic:
                        if (!p.isArray) { cell = null; return false; }
                        cell.value = p.arraySize.ToString(CultureInfo.InvariantCulture);
                        break;
                    default: cell = null; return false;
                }
                return true;
            }

            internal bool Same(QvSceneCell other) => other != null && type == other.type
                && value == other.value && reference == other.reference;

            internal void Write(SerializedProperty p, string stored, UnityEngine.Object objectReference)
            {
                switch (type)
                {
                    case SerializedPropertyType.Integer: p.longValue = long.Parse(stored, CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: p.boolValue = stored == "1"; break;
                    case SerializedPropertyType.Float: p.doubleValue = double.Parse(stored, CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.String: p.stringValue = stored; break;
                    case SerializedPropertyType.Enum: p.enumValueIndex = int.Parse(stored, CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.ObjectReference: p.objectReferenceValue = objectReference; break;
                    case SerializedPropertyType.Vector2: p.vector2Value = JsonUtility.FromJson<Vector2Value>(stored).value; break;
                    case SerializedPropertyType.Vector3: p.vector3Value = JsonUtility.FromJson<Vector3Value>(stored).value; break;
                    case SerializedPropertyType.Vector4: p.vector4Value = JsonUtility.FromJson<Vector4Value>(stored).value; break;
                    case SerializedPropertyType.Quaternion: p.quaternionValue = JsonUtility.FromJson<QuaternionValue>(stored).value; break;
                    case SerializedPropertyType.Color: p.colorValue = JsonUtility.FromJson<ColorValue>(stored).value; break;
                    case SerializedPropertyType.Generic: if (p.isArray) p.arraySize = int.Parse(stored, CultureInfo.InvariantCulture); break;
                }
            }

            internal static QvSceneCell FromChange(QvScenePropertyChange change, bool installed) => new QvSceneCell
            {
                type = (SerializedPropertyType)change.propertyType,
                value = installed ? change.installed : change.before,
                reference = installed ? change.installedReference : change.beforeReference
            };

            [Serializable] private struct Vector2Value { public Vector2 value; }
            [Serializable] private struct Vector3Value { public Vector3 value; }
            [Serializable] private struct Vector4Value { public Vector4 value; }
            [Serializable] private struct QuaternionValue { public Quaternion value; }
            [Serializable] private struct ColorValue { public Color value; }
        }
    }

    public static class QvSceneRecorder
    {
        public static string NormalizedDump(Scene scene)
        {
            // Layout components can write delayed RectTransform values immediately
            // after a scene is opened. Settle them on both sides of a comparison.
            Canvas.ForceUpdateCanvases();
            var snapshot = QvSceneSnapshot.Capture(scene);
            var builder = new StringBuilder();
            foreach (var go in snapshot.objects.Where(o => o != null && o.GetComponent<QvSceneInstallRecord>() == null
                && !HasRecordAncestor(o)).OrderBy(o => snapshot.objectPaths[o], StringComparer.Ordinal))
            {
                var path = snapshot.objectPaths[go];
                Append(go, path);
                var components = go.GetComponents<Component>();
                for (int index = 0; index < components.Length; index++)
                    if (components[index] != null)
                        Append(components[index], path + "/" + components[index].GetType().FullName + "#" + index);
            }
            return builder.ToString();

            bool HasRecordAncestor(GameObject item)
            {
                for (var parent = item.transform.parent; parent != null; parent = parent.parent)
                    if (parent.GetComponent<QvSceneInstallRecord>() != null) return true;
                return false;
            }

            void Append(UnityEngine.Object target, string label)
            {
                builder.AppendLine(label);
                if (!snapshot.values.TryGetValue(target, out var values)) return;
                foreach (var entry in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var cell = entry.Value;
                    var reference = cell.reference;
                    string value = cell.value;
                    if (reference != null)
                    {
                        var referenceGo = reference as GameObject;
                        if (referenceGo == null && reference is Component component) referenceGo = component.gameObject;
                        value = referenceGo != null && snapshot.objectPaths.TryGetValue(referenceGo, out var refPath)
                            ? refPath + "/" + reference.GetType().FullName
                            : AssetDatabase.GetAssetPath(reference) + "/" + reference.name;
                    }
                    builder.Append(entry.Key).Append('=').Append(value).Append('|')
                        .Append(cell.prefabOverride ? '1' : '0').AppendLine();
                }
            }
        }

        public static QvSceneInstallRecord Find(Scene scene, string featureId)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var record = root.GetComponent<QvSceneInstallRecord>();
                if (record != null && record.featureId == featureId) return record;
            }
            return null;
        }

        public static QvSceneInstallRecord Install(Scene scene, string featureId, string optionsJson, Action action)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("保存済みのロードされたシーンが必要です");
            var existing = Find(scene, featureId);
            var before = QvSceneSnapshot.Capture(scene);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(featureId + " Add to Scene");
            // Setup may change pens and managers regardless of their names. A partial
            // registration makes rollback destructive in large scenes.
            var undoTargets = before.values.Keys.Where(target => target != null).ToArray();
            if (undoTargets.Length > 0)
                Undo.RegisterCompleteObjectUndo(undoTargets, featureId + " setup");
            try
            {
                action();
                var after = QvSceneSnapshot.Capture(scene);
                var record = existing;
                if (record == null)
                {
                    var recordObject = new GameObject(featureId + " 導入記録 (EditorOnly)");
                    recordObject.tag = "EditorOnly";
                    Undo.RegisterCreatedObjectUndo(recordObject, featureId + " install record");
                    record = Undo.AddComponent<QvSceneInstallRecord>(recordObject);
                    record.featureId = featureId;
                    record.packageVersion = "0.1.0";
                }
                else Undo.RecordObject(record, featureId + " install record");
                record.optionsJson = optionsJson;
                var newObjects = after.objects.Except(before.objects).ToArray();
                foreach (var deleted in before.objects.Except(after.objects))
                {
                    var label = before.objectPaths[deleted];
                    if (!record.deletedDuringAdd.Contains(label)) record.deletedDuringAdd.Add(label);
                }
                foreach (var go in newObjects)
                {
                    if (!record.createdObjects.Contains(go)) record.createdObjects.Add(go);
                    if (go.transform.parent == null || !newObjects.Contains(go.transform.parent.gameObject))
                        Undo.RegisterCreatedObjectUndo(go, featureId + " object");
                }
                foreach (var component in after.components.Except(before.components))
                    {
                        if (!record.createdComponents.Contains(component)) record.createdComponents.Add(component);
                        if (newObjects.Contains(component.gameObject)) continue;
                        Undo.RegisterCreatedObjectUndo(component, featureId + " component");
                    }
                if (existing == null) record.createdComponentInventoryComplete = true;
                foreach (var pair in before.values)
                {
                    if (pair.Key == null || !after.values.TryGetValue(pair.Key, out var newer)) continue;
                    var owner = pair.Key as GameObject;
                    if (owner == null && pair.Key is Component ownedComponent) owner = ownedComponent.gameObject;
                    if (owner != null && (record.createdObjects.Contains(owner) || newObjects.Contains(owner))) continue;
                    if (pair.Key is Component existingComponent && record.createdComponents.Contains(existingComponent)) continue;
                    foreach (var property in pair.Value.OrderBy(p => p.Key.EndsWith(".Array.size", StringComparison.Ordinal) ? 0 : 1))
                    {
                        if (!newer.TryGetValue(property.Key, out var installed))
                        {
                            if (property.Key.Contains(".Array.data[")
                                && !record.changes.Any(c => c.target == pair.Key && c.path == property.Key))
                                record.changes.Add(new QvScenePropertyChange
                                {
                                    target = pair.Key, path = property.Key, propertyType = (int)property.Value.type,
                                    before = property.Value.value, beforeReference = property.Value.reference,
                                    hadPrefabOverride = property.Value.prefabOverride, removedDuringAdd = true
                                });
                            continue;
                        }
                        if (property.Value.Same(installed)) continue;
                        var previousChange = record.changes.FirstOrDefault(c =>
                            c.target == pair.Key && c.path == property.Key);
                        if (previousChange != null)
                        {
                            previousChange.installed = installed.value;
                            previousChange.installedReference = installed.reference;
                            continue;
                        }
                        record.changes.Add(new QvScenePropertyChange
                        {
                            target = pair.Key, path = property.Key, propertyType = (int)property.Value.type,
                            before = property.Value.value, installed = installed.value,
                            beforeReference = property.Value.reference, installedReference = installed.reference,
                            hadPrefabOverride = property.Value.prefabOverride
                        });
                    }
                }
                foreach (var go in after.networkObjects.Except(before.networkObjects))
                    if (!record.networkObjects.Contains(go)) record.networkObjects.Add(go);
                EditorUtility.SetDirty(record);
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
                return record;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                var remaining = QvSceneSnapshot.Capture(scene);
                foreach (var component in remaining.components.Except(before.components).ToArray())
                    if (component != null && before.objects.Contains(component.gameObject))
                        UnityEngine.Object.DestroyImmediate(component);
                foreach (var go in remaining.objects.Except(before.objects)
                    .OrderByDescending(item =>
                    {
                        int depth = 0;
                        for (var parent = item.transform.parent; parent != null; parent = parent.parent) depth++;
                        return depth;
                    }).ToArray())
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                // Undo can queue a layout rebuild, which writes RectTransform values
                // after the undo group is reverted. Flush it before restoring cells.
                Canvas.ForceUpdateCanvases();
                QvSceneSnapshot.Restore(before);
                throw;
            }
        }

        public static QvSceneInstallRecord ImportExisting(Scene scene, string featureId,
            Func<GameObject, bool> belongsToFeature)
        {
            if (Find(scene, featureId) != null) throw new InvalidOperationException("導入記録は既にあります: " + featureId);
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("保存済みのロードされたシーンが必要です");
            var targets = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(transform => transform.gameObject).Where(belongsToFeature)
                .SelectMany(go => go.GetComponentsInChildren<Transform>(true).Select(child => child.gameObject))
                .Distinct().ToArray();
            if (targets.Length == 0) throw new InvalidOperationException("取り込み対象がありません: " + featureId);
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            var go = new GameObject(featureId + " 導入記録 (EditorOnly)");
            go.tag = "EditorOnly";
            Undo.RegisterCreatedObjectUndo(go, featureId + " import record");
            var record = Undo.AddComponent<QvSceneInstallRecord>(go);
            record.featureId = featureId;
            record.packageVersion = "0.1.0";
            record.optionsJson = "{}";
            record.createdObjects.AddRange(targets);
            foreach (var target in targets)
                record.createdComponents.AddRange(target.GetComponents<Component>().Where(c => c != null));
            record.createdComponentInventoryComplete = true;
            var snapshot = QvSceneSnapshot.Capture(scene);
            foreach (var target in snapshot.networkObjects)
                if (record.createdObjects.Contains(target) || record.createdObjects.Any(o => target.transform.IsChildOf(o.transform)))
                    record.networkObjects.Add(target);
            record.irreversibleChanges.Add("旧シーンの既存値は導入前の値が不明なため自動復元できません");
            EditorUtility.SetDirty(record);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            return record;
        }

        public static List<string> Integrity(Scene scene, string featureId)
        {
            var record = Find(scene, featureId);
            var issues = new List<string>();
            if (record == null) { issues.Add("導入記録がありません"); return issues; }
            if (record.createdObjects.Any(o => o == null)) issues.Add("記録した GameObject が削除されています");
            if (record.createdComponents.Any(o => o == null)) issues.Add("記録した Component が削除されています");
            return issues;
        }

        public static void CopyProxies(Scene scene)
        {
            var proxyType = Type.GetType("UdonSharp.UdonSharpBehaviour, UdonSharp.Runtime");
            var utility = Type.GetType("UdonSharpEditor.UdonSharpEditorUtility, UdonSharp.Editor");
            if (proxyType == null || utility == null) return;
            var method = utility.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "CopyProxyToUdon"
                    && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == proxyType
                    && m.GetParameters().Skip(1).All(p => p.IsOptional));
            if (method == null) return;
            var parameters = method.GetParameters();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var proxy in root.GetComponentsInChildren<Component>(true))
                    if (proxy != null && proxyType.IsInstanceOfType(proxy))
                    {
                        var args = new object[parameters.Length];
                        args[0] = proxy;
                        for (var i = 1; i < args.Length; ++i) args[i] = parameters[i].DefaultValue;
                        try { method.Invoke(null, args); }
                        catch (Exception e)
                        {
                            // 新規プロジェクトでは UdonSharp のコンパイル完了前に呼ばれ、同期が失敗することがある。
                            // 値はプロキシ側に残り、コンパイル後・ビルド時に UdonSharp が同期するため、導入は中止しない。
                            UnityEngine.Debug.LogWarning("[QvSceneSetup] '" + proxy.name + "' の Udon への即時同期をスキップしました: "
                                + (e.InnerException ?? e).Message);
                        }
                    }
        }
    }
}
