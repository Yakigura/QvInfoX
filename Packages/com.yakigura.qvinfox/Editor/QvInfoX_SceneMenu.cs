using System;
using System.Linq;
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
                EditorUtility.DisplayDialog("QvInfoX", "導入記録がありません。既存シーンを取り込む (Import Existing Scene) を先に実行してください。", "OK");
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

        #if QVINFOX_DIAG
        [MenuItem("Tools/Yakigura/QvInfoX/シーンを検証 (Validate Scene)")]
        #endif
        public static void Validate()
        {
            var issues = QvInfoX_SceneInstaller.Validate(SceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("QvInfoX シーン検証",
                issues.Count == 0 ? "PASS" : string.Join("\n", issues), "OK");
            if (issues.Count == 0) Debug.Log("QVINFOX_SCENE_VALIDATE PASS");
            else Debug.LogError("QVINFOX_SCENE_VALIDATE FAIL\n" + string.Join("\n", issues));
        }

        #if QVINFOX_DIAG
        [MenuItem("Tools/Yakigura/QvInfoX/導入設定... (Setup Window)")]
        #endif
        public static void OpenWindow() => EditorWindow.GetWindow<QvInfoXSetupWindow>("QvInfoX 導入設定");

        #if QVINFOX_DIAG
        [MenuItem("Tools/Yakigura/QvInfoX/既存シーンを取り込む (Import Existing Scene)")]
        #endif
        public static void ImportExisting()
        {
            if (!EditorUtility.DisplayDialog("QvInfoX 旧シーン取り込み",
                "既存オブジェクトを導入物として記録します。導入前の設定は不明なため完全には復元できません。",
                "取り込む", "キャンセル")) return;
            QvInfoX_SceneInstaller.ImportExisting(SceneManager.GetActiveScene());
        }
    }

    public sealed class QvInfoXSetupWindow : EditorWindow
    {
        private QvInfoXSetupOptions options = new QvInfoXSetupOptions();

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("現在のシーンに QvInfoX を追加・検証・削除します。", MessageType.Info);
            options.Placement = (QvInfoXMonitorPlacement)EditorGUILayout.EnumPopup("Monitor 位置", options.Placement);
            if (options.Placement == QvInfoXMonitorPlacement.SpawnRelative)
                options.Offset = EditorGUILayout.Vector3Field("spawn 基準 (右・上・前)", options.Offset);
            if (options.Placement == QvInfoXMonitorPlacement.Absolute)
            {
                options.AbsolutePosition = EditorGUILayout.Vector3Field("座標", options.AbsolutePosition);
                options.AbsoluteYaw = EditorGUILayout.FloatField("Yaw", options.AbsoluteYaw);
            }
            options.OverrideExistingPlacement = EditorGUILayout.Toggle("既存 Monitor も移動", options.OverrideExistingPlacement);
            options.HoldInitialState = EditorGUILayout.Popup(new GUIContent(
                "QvHold 入室時 / On entry / 입장 시",
                "新規生成時だけ使用。既存 Controller の設定は保持します。 / Creation only; preserves existing Controller settings. / 새로 생성할 때만 사용. 기존 Controller 설정 유지."),
                options.HoldInitialState, new[] {
                    "シーンの設定のまま / Keep scene settings / 씬 설정 유지",
                    "保持 ON / Hold ON / 유지 ON",
                    "保持 OFF / Hold OFF / 유지 OFF" });
            if (GUILayout.Button("追加 / 更新")) QvInfoX_SceneInstaller.Add(SceneManager.GetActiveScene(), options);
            if (GUILayout.Button("削除予定を確認"))
            {
                var scene = SceneManager.GetActiveScene();
                var record = QvSceneRecorder.Find(scene, QvInfoX_SceneInstaller.FeatureId);
                EditorUtility.DisplayDialog("QvInfoX", record == null
                    ? "未導入です。追加すると PlayerPool、WorldState、Monitor、Indicator を作成します。"
                    : QvInfoX_SceneInstaller.DryRun(scene).ToString(), "OK");
            }
            if (GUILayout.Button("取り除く")) QvInfoX_SceneMenu.Remove();
            if (GUILayout.Button("検証")) QvInfoX_SceneMenu.Validate();
        }
    }
}
