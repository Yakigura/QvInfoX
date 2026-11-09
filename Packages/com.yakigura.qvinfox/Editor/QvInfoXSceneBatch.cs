using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yakigura.QvSceneSetup.Editor;

namespace QvInfoX.Editor
{
    public static class QvInfoXSceneBatch
    {
        public static void AddAndExit() => Run("ADD", scene => QvInfoX_SceneInstaller.Add(scene));
        public static void RemoveAndExit() => Run("REMOVE", scene =>
        {
            bool force = Arg("-qvForce") == "1";
            var plan = QvInfoX_SceneInstaller.DryRun(scene);
            if (!force && plan.conflicts.Count != 0)
                throw new InvalidOperationException("Remove conflicts: " + string.Join(", ", plan.conflicts));
            QvInfoX_SceneInstaller.Remove(scene, force);
        });
        public static void ImportAndExit() => Run("IMPORT", QvInfoX_SceneInstaller.ImportExisting);
        public static void HealthCheckAndExit() => Run("VALIDATE", scene =>
        {
            var issues = QvInfoX_SceneInstaller.Validate(scene);
            if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues));
        });
        public static void ValidateAndExit() => HealthCheckAndExit();
        public static void DryRunAndExit() => Run("DRYRUN", scene =>
        {
            var json = Arg("-qvOp") == "remove"
                ? JsonUtility.ToJson(new JsonPlan(QvInfoX_SceneInstaller.DryRun(scene)), true)
                : "{\"operation\":\"add\",\"scene\":\"" + scene.path + "\"}";
            var output = Arg("-qvOut");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, json);
            Debug.Log(json);
        });

        [Serializable]
        private sealed class JsonPlan
        {
            public string[] delete;
            public string[] restore;
            public string[] conflicts;
            public string[] warnings;
            public JsonPlan(QvSceneRemovePlan plan)
            {
                delete = plan.delete.ToArray(); restore = plan.restore.ToArray();
                conflicts = plan.conflicts.ToArray(); warnings = plan.warnings.ToArray();
            }
        }

        private static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == key) return args[i + 1];
            return null;
        }

        private static void Run(string action, Action<Scene> body)
        {
            try
            {
                foreach (var path in (Arg("-qvScene") ?? SceneManager.GetActiveScene().path).Split(';'))
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    body(scene);
                    if (Arg("-qvSave") == "1" && action != "VALIDATE") EditorSceneManager.SaveScene(scene);
                    Debug.Log("QVINFOX_SCENE_" + action + " PASS " + path);
                }
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("QVINFOX_SCENE_" + action + " FAIL " + ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
