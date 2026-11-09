using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yakigura.QvSceneSetup.Editor;

namespace QvInfoX.Editor
{
    public static class QvInfoX_SceneMenu
    {
        [MenuItem("Tools/Yakigura/QvInfoX/シーンに追加・更新 (Auto Setup)", false, 100)]
        public static void Add() => QvInfoX_SceneInstaller.Add(SceneManager.GetActiveScene());

        [MenuItem("Tools/Yakigura/QvInfoX/シーンから取り除く (Remove from Scene)", false, 101)]
        public static void Remove()
        {
            var scene = SceneManager.GetActiveScene();
            if (QvSceneRecorder.Find(scene, QvInfoX_SceneInstaller.FeatureId) == null)
            {
                EditorUtility.DisplayDialog("QvInfoX", "導入記録がないため、自動で取り除けません。", "OK");
                return;
            }
            var plan = QvInfoX_SceneInstaller.DryRun(scene);
            if (!EditorUtility.DisplayDialog("QvInfoX を取り除く", plan.ToString()
                + "\n生成アセットは他シーンでも使うため残します。", "取り除く", "キャンセル")) return;
            var force = plan.conflicts.Count > 0 && EditorUtility.DisplayDialog("変更の競合",
                "導入後に変更された値があります。\n" + string.Join("\n", plan.conflicts),
                "強制復元", "変更を残す");
            var result = QvInfoX_SceneInstaller.Remove(scene, force);
            foreach (var warning in result.warnings) Debug.LogWarning(warning);
        }

    }
}
