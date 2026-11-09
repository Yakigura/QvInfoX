// ----------------------------------------------------------------------
// QvInfoX - Non-invasive QvPen monitoring tools
// ----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using VRC.SDKBase;
using VRC.SDK3.Components;
using QvPen.UdonScript;
using UdonSharp;
using UdonSharpEditor;
using Cyan.PlayerObjectPool;
using Yakigura.QvInkPreview;
using Yakigura.QvInkPreview.Editor;
using Yakigura.QvSceneSetup.Editor;
using UnityEngine.SceneManagement;

namespace QvInfoX.Editor
{
    /// <summary>
    /// QvInfoX の自動シーンセットアップ。
    /// QvPen 本体 (net.ureishi.qvpen) のソース / シェーダー / Prefab は一切変更せず、
    /// 公開 API (QvPen_Pen.inkPoolRootName, QvPen_LateSync.pen/InkPoolSynced/InkPoolNotSynced,
    /// QvPen_PenManager.colorGradient/inkWidth) と、SerializedObject 経由の読み取り専用参照
    /// (pointer / trailRenderer 等、GameObject 階層上に実在するコンポーネント) のみを用いて
    /// Indicator と Monitor パネルを構築・配線する。
    /// QvHold の任意移行は確認後にシーン内 Pickup だけを SDK の移行処理で変更する。
    /// </summary>
    public static class QvInfoX_AutoSetup
    {
        private static T[] FindSceneObjects<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }

        private static void CollectPensInHierarchy(Transform node, List<QvPen_Pen> result)
        {
            var pen = node.GetComponent<QvPen_Pen>();
            if (pen != null) result.Add(pen);
            for (var i = 0; i < node.childCount; i++)
                CollectPensInHierarchy(node.GetChild(i), result);
        }

        public const string MONITOR_ROOT_NAME = "QvInfoX_Monitor";
        public const string WORLD_STATE_ROOT_NAME = "QvInfoX_WorldState";
        public const string MASTER_TOTALS_ROOT_NAME = "QvInfoX_MasterTotals";
        public const string UDON_ASSET_DIRECTORY = "Packages/com.yakigura.qvinfox/Runtime/UdonAsset";
        public const string INDICATOR_CHILD_NAME = "QvInfoX_Indicator";
        public const int WalkthroughLayer = 17;

       public static void AutoSetupMenu()
        {
            QvInfoX_SceneInstaller.Add(SceneManager.GetActiveScene());
        }

        public static void PerformSetup(bool initiallyEnableHold = false, int holdInitialState = 0)
        {
            QvInfoX_SceneInstaller.ValidateInfrastructureLayout(SceneManager.GetActiveScene());
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                PerformSetupPrepared(initiallyEnableHold, holdInitialState);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        internal static void PerformSetupPrepared(bool initiallyEnableHold, int holdInitialState = 0)
        {
            AssetDatabase.Refresh();
            EnsureAllProgramAssets();
            EnsureEventSystem();
            EnsurePlayerObjectPool();
            var worldState = SetupWorldState();
            var masterTotals = SetupMasterTotals();

            var inkPreviewManager = QvInkPreview_AutoSetup.AutoSetup();
            var indicators = SetupAllIndicators();
            var manager = SetupMonitorPrepared(indicators, initiallyEnableHold, holdInitialState);
            if (manager != null)
            {
                var managerSo = new SerializedObject(manager);
                managerSo.FindProperty("worldState").objectReferenceValue = worldState;
                managerSo.FindProperty("masterTotals").objectReferenceValue = masterTotals;
                managerSo.FindProperty("inkPreviewManager").objectReferenceValue = inkPreviewManager;
                var pensInHierarchy = new List<QvPen_Pen>();
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    CollectPensInHierarchy(root.transform, pensInHierarchy);
                var penOrderProp = managerSo.FindProperty("penOrder");
                penOrderProp.arraySize = pensInHierarchy.Count;
                for (var i = 0; i < pensInHierarchy.Count; i++)
                    penOrderProp.GetArrayElementAtIndex(i).objectReferenceValue = pensInHierarchy[i];
                managerSo.ApplyModifiedProperties();
                SafeCopyProxyToUdon(manager);
            }

            if (!Application.isPlaying && manager != null)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }

            Debug.Log("[QvInfoX AutoSetup] " + LanguageText(manager != null ? manager.language : 0,
                $"セットアップ完了: Indicator {indicators.Count} 個を検出/配線しました。",
                $"Setup complete: {indicators.Count} indicators connected.",
                $"설치 완료: 인디케이터 {indicators.Count}개를 연결했습니다."));
        }

        #if QVINFOX_DIAG
        [MenuItem("Tools/Yakigura/QvInfoX/Health Check")]
        #endif
        public static void HealthCheckMenu()
        {
            var issues = CollectHealthIssues();
            var root = FindSceneRoot(MONITOR_ROOT_NAME);
            var manager = root != null ? root.GetComponent<QvInfoX_Manager>() : null;
            var language = manager != null ? manager.language : 0;
            if (issues.Count == 0)
                Debug.Log("[QvInfoX Health Check] " + LanguageText(language,
                    "PASS: 配置と配線に問題は見つかりませんでした。",
                    "PASS: Scene placement and references are healthy.",
                    "PASS: 배치와 참조 연결에 문제가 없습니다."));
            else
            {
                for (var i = 0; i < issues.Count; i++)
                    issues[i] = HealthIssueText(issues[i], language);
                Debug.LogWarning("[QvInfoX Health Check] " + issues.Count + " "
                    + LanguageText(language, "件の問題:", "issues:", "개 문제:") + "\n- "
                    + string.Join("\n- ", issues) + "\n"
                    + LanguageText(language,
                        "Tools/Yakigura/QvInfoX/シーンに追加・更新 (Auto Setup) で修復できます。",
                        "Run Tools/Yakigura/QvInfoX/シーンに追加・更新 (Auto Setup) to repair them.",
                        "Tools/Yakigura/QvInfoX/シーンに追加・更新 (Auto Setup)을 실행하여 복구하세요."));
            }
        }

        public static List<string> CollectHealthIssues()
        {
            var issues = new List<string>();
            var active = SceneManager.GetActiveScene();
            try { QvInfoX_SceneInstaller.ValidateInfrastructureLayout(active); }
            catch (InvalidOperationException ex) { issues.Add(ex.Message); return issues; }
            if (QvSceneRecorder.Find(active, QvInfoX_SceneInstaller.FeatureId) != null)
                issues.AddRange(QvSceneRecorder.Integrity(active, QvInfoX_SceneInstaller.FeatureId));
            foreach (var group in FindSceneObjects<QvInkPreview_Pen>(true)
                .Where(preview => preview.gameObject.scene == active && preview.Pen != null)
                .GroupBy(preview => preview.Pen))
                if (group.Count() > 1)
                    issues.Add(group.Key.name + ": QvInkPreview が " + group.Count() + " 個あります");
            var worldStates = FindSceneObjects<QvInfoX_WorldState>(true);
            var stateCount = 0;
            QvInfoX_WorldState sceneState = null;
            var stateScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var state in worldStates)
                if (state.gameObject.scene == stateScene)
                {
                    sceneState = state;
                    stateCount++;
                }
            if (stateCount != 1) issues.Add($"WorldState: 1 本必要です (現在 {stateCount})");
            else if (!sceneState.gameObject.activeSelf)
                issues.Add("WorldState が無効です");
            var totals = FindSceneObjects<QvInfoX_MasterTotals>(true);
            var totalsCount = 0;
            QvInfoX_MasterTotals sceneTotals = null;
            foreach (var item in totals)
                if (item.gameObject.scene == stateScene)
                {
                    sceneTotals = item;
                    totalsCount++;
                }
            if (totalsCount != 1) issues.Add($"MasterTotals: 1 本必要です (現在 {totalsCount})");
            else if (!sceneTotals.gameObject.activeSelf)
                issues.Add("MasterTotals が無効です");
            if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
                issues.Add("EventSystem がありません");

            var poolRoot = FindSceneRoot(PLAYER_POOL_ROOT_NAME);
            if (poolRoot == null)
                issues.Add("PlayerPool がありません");
            else
            {
                if (!poolRoot.activeSelf) issues.Add("PlayerPool が無効です");
                var pool = poolRoot.GetComponent<CyanPlayerObjectPool>();
                var assigner = poolRoot.GetComponentInChildren<CyanPlayerObjectAssigner>(true);
                var slotsParent = poolRoot.transform.Find("Assigner/PlayerSlots");
                if (pool == null) issues.Add("PlayerPool: CyanPlayerObjectPool がありません");
                if (assigner == null) issues.Add("PlayerPool: Assigner がありません");
                if (slotsParent == null) issues.Add("PlayerPool: PlayerSlots がありません");
                if (pool != null && slotsParent != null)
                {
                    if (pool.poolSize != slotsParent.childCount)
                        issues.Add($"PlayerPool: poolSize={pool.poolSize}, slot数={slotsParent.childCount}");
                    if (assigner != null && assigner.poolObjectsParent != slotsParent)
                        issues.Add("PlayerPool: Assigner の poolObjectsParent が違います");
                    for (var i = 0; i < slotsParent.childCount; i++)
                        if (slotsParent.GetChild(i).GetComponent<QvInfoX_PlayerSlot>() == null)
                            issues.Add($"PlayerPool: slot {i} に QvInfoX_PlayerSlot がありません");
                }
            }

            var root = FindSceneRoot(MONITOR_ROOT_NAME);
            if (root == null)
            {
                issues.Add("Monitor がありません");
                return issues;
            }

            var manager = root.GetComponent<QvInfoX_Manager>();
            if (!root.activeSelf) issues.Add("Monitor が無効です");
            var rb = root.GetComponent<Rigidbody>();
            var pickup = root.GetComponent<VRCPickup>();
            var handle = root.GetComponent<BoxCollider>();
            if (manager == null) issues.Add("Monitor: Manager がありません");
            if (rb == null || !rb.isKinematic || rb.useGravity)
                issues.Add("Monitor: Rigidbody の Kinematic/Gravity 設定が違います");
            if (pickup == null || !pickup.pickupable)
                issues.Add("Monitor: VRC_Pickup がありません、または無効です");
            if (handle == null || handle.isTrigger || handle.center.y >= -0.31f)
                issues.Add("Monitor: Root の持ち手 Collider がありません、または操作面と重なります");
            CheckPanelColliders(root.transform, issues);

            var canvas = root.transform.Find("Canvas");
            if (canvas == null)
                issues.Add("Monitor: Canvas がありません");
            else
            {
                if (canvas.GetComponent<Canvas>() == null || canvas.GetComponent<Canvas>().renderMode != RenderMode.WorldSpace)
                    issues.Add("Canvas: World Space 設定がありません");
                if (canvas.GetComponent<GraphicRaycaster>() == null || !canvas.GetComponent<GraphicRaycaster>().enabled)
                    issues.Add("Canvas: GraphicRaycaster がありません、または無効です");
                if (canvas.GetComponent<VRCUiShape>() == null || canvas.GetComponent<BoxCollider>() == null)
                    issues.Add("Canvas: VRCUiShape または Collider がありません");
                foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer != WalkthroughLayer)
                    {
                        issues.Add("Canvas: Walkthrough 以外の Layer があります: " + t.name);
                        break;
                    }
            }

            if (manager != null)
            {
                if (manager.worldState != sceneState)
                    issues.Add("Manager: WorldState 参照が未配線です");
                if (manager.masterTotals != sceneTotals)
                    issues.Add("Manager: MasterTotals 参照が未配線です");
                var allSlots = FindSceneObjects<QvInfoX_PlayerSlot>(true);
                var sceneSlots = new List<QvInfoX_PlayerSlot>();
                foreach (var slot in allSlots)
                    if (slot.gameObject.scene == root.scene)
                        sceneSlots.Add(slot);
                if (manager.playerSlots == null || manager.playerSlots.Length != sceneSlots.Count)
                    issues.Add("Manager: PlayerSlot 配列の長さがシーンと一致しません");
                else
                    foreach (var slot in sceneSlots)
                        if (Array.IndexOf(manager.playerSlots, slot) < 0)
                        {
                            issues.Add("Manager: PlayerSlot 参照が不足しています");
                            break;
                        }

                CheckUiArray(issues, "rowPool", manager.rowPool, PLAYER_POOL_SIZE);
                CheckUiArray(issues, "rowMetaTexts", manager.rowMetaTexts, PLAYER_POOL_SIZE);
                CheckUiArray(issues, "rowAuthoredTexts", manager.rowAuthoredTexts, PLAYER_POOL_SIZE);
                CheckUiArray(issues, "rowProgressBars", manager.rowProgressBars, PLAYER_POOL_SIZE);
                CheckUiArray(issues, "rowPercentTexts", manager.rowPercentTexts, PLAYER_POOL_SIZE);
                CheckUiArray(issues, "detailInkLines", manager.detailInkLines, 40);
                CheckUiArray(issues, "detailProgressBars", manager.detailProgressBars, 40);
                CheckUiArray(issues, "detailPercentTexts", manager.detailPercentTexts, 40);
                CheckUiArray(issues, "detailRowTexts", manager.detailRowTexts, 40);
                CheckUiArray(issues, "detailMetaTexts", manager.detailMetaTexts, 40);
                CheckRequiredUiReference(issues, "rowsContainer", manager.rowsContainer);
                CheckRequiredUiReference(issues, "top3Text", manager.top3Text);
                CheckRequiredUiReference(issues, "footerStatusText", manager.footerStatusText);
                CheckRequiredUiReference(issues, "manualButtonText", manager.manualButtonText);
                CheckRequiredUiReference(issues, "resetPositionButtonText", manager.resetPositionButtonText);
                CheckRequiredUiReference(issues, "indicatorToggleText", manager.indicatorToggleText);
                CheckRequiredUiReference(issues, "mainViewRoot", manager.mainViewRoot);
                CheckRequiredUiReference(issues, "detailViewRoot", manager.detailViewRoot);
                CheckRequiredUiReference(issues, "mainTabImage", manager.mainTabImage);
                CheckRequiredUiReference(issues, "detailTabImage", manager.detailTabImage);
                var managerSerialized = new SerializedObject(manager);
                foreach (var fieldName in new[] { "manualUpdateButtonRect", "mainTabRect",
                    "detailTabRect", "indicatorToggleRect", "languageButtonRect", "resetPositionButtonRect" })
                {
                    var field = managerSerialized.FindProperty(fieldName);
                    if (field == null || field.objectReferenceValue == null)
                        issues.Add("Manager: " + fieldName + " is not wired");
                }
                if (manager.mainScrollRect == null || manager.detailScrollRect == null
                    || manager.manualUpdateButton == null || manager.indicatorToggle == null
                    || manager.languageButton == null || manager.languageButtonText == null
                    || manager.resetPositionButton == null || !manager.initialPoseCaptured
                    || manager.mainTabText == null || manager.detailTabText == null)
                    issues.Add("Manager: Scroll/Button/Toggle の参照が不足しています");
                else
                {
                    CheckListener(issues, manager.manualUpdateButton.onClick, manager.manualUpdateButton,
                        "m_OnClick", manager, nameof(QvInfoX_Manager._OnManualUpdateButton), "手動更新");
                    CheckListener(issues, manager.resetPositionButton.onClick, manager.resetPositionButton,
                        "m_OnClick", manager, nameof(QvInfoX_Manager._ResetPosition), "Reset Position");
                    CheckListener(issues, manager.indicatorToggle.onValueChanged, manager.indicatorToggle,
                        "onValueChanged", manager, nameof(QvInfoX_Manager._OnIndicatorToggleChanged), "Indicator Toggle");
                    CheckListener(issues, manager.languageButton.onClick, manager.languageButton,
                        "m_OnClick", manager, nameof(QvInfoX_Manager._CycleLanguage), "Language Button");
                }
                var mainTab = root.transform.Find("Canvas/Panel/Tabs/MainTabButton");
                var detailTab = root.transform.Find("Canvas/Panel/Tabs/DetailTabButton");
                if (mainTab == null || mainTab.GetComponent<Button>() == null
                    || detailTab == null || detailTab.GetComponent<Button>() == null)
                    issues.Add("Monitor: タブボタンが不足しています");
                else
                {
                    var mainButton = mainTab.GetComponent<Button>();
                    var detailButton = detailTab.GetComponent<Button>();
                    CheckListener(issues, mainButton.onClick, mainButton, "m_OnClick",
                        manager, nameof(QvInfoX_Manager._ShowMainView), "プレイヤータブ");
                    CheckListener(issues, detailButton.onClick, detailButton, "m_OnClick",
                        manager, nameof(QvInfoX_Manager._ShowDetailView), "ペン詳細タブ");
                }
            }

            var penManagers = FindSceneObjects<QvPen_PenManager>(true);
            var expectedIndicatorCount = 0;
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var pm in penManagers)
            {
                if (pm.gameObject.scene != activeScene) continue;
                var pen = pm.GetComponentInChildren<QvPen_Pen>(true);
                if (pen == null) continue;
                expectedIndicatorCount++;
                var matches = FindSceneObjects<QvInfoX_Indicator>(true);
                var count = 0;
                foreach (var indicator in matches)
                    if (indicator.Pen == pen || indicator.transform.IsChildOf(pen.transform))
                    {
                        count++;
                        CheckPanelColliders(indicator.transform, issues);
                        if (indicator.Pen != pen || indicator.PenManager != pm || indicator.InkPreviewTexture == null
                            || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(indicator.InkPreviewTexture)))
                            issues.Add("Indicator: " + pen.name + " の参照または生成プレビューが不正です");
                    }
                if (count != 1) issues.Add($"Indicator: {pen.name} は {count} 個です（期待値 1）");
            }
            if (sceneState != null && sceneState.hasMasterTotals
                && (sceneState.masterPenStrokes == null
                    || sceneState.masterPenStrokes.Length != expectedIndicatorCount))
            {
                var reportedCount = sceneState.masterPenStrokes == null
                    ? 0 : sceneState.masterPenStrokes.Length;
                issues.Add($"WorldState: ペン配列長 {reportedCount} がシーンのペン数 {expectedIndicatorCount} と一致しません");
            }
            if (manager != null && (manager.indicators == null
                || manager.indicators.Length != expectedIndicatorCount))
                issues.Add("Manager: Indicator 配列の長さがペン数と一致しません");
            else if (manager != null)
            {
                foreach (var indicator in FindSceneObjects<QvInfoX_Indicator>(true))
                    if (indicator.gameObject.scene == root.scene && Array.IndexOf(manager.indicators, indicator) < 0)
                    {
                        issues.Add("Manager: Indicator 参照が不足しています");
                        break;
                    }
            }

            return issues;
        }

        public static List<string> CollectHealthIssues(Scene scene)
        {
            var previous = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                return CollectHealthIssues();
            }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        private static void CheckUiArray<T>(List<string> issues, string name, T[] values, int expected)
            where T : UnityEngine.Object
        {
            if (values == null || values.Length != expected)
            {
                issues.Add($"Manager: {name} は {expected} 要素必要です");
                return;
            }
            for (var i = 0; i < values.Length; i++)
                if (values[i] == null)
                {
                    issues.Add($"Manager: {name}[{i}] が未配線です");
                    break;
                }
        }

        private static void CheckRequiredUiReference(List<string> issues, string name, UnityEngine.Object value)
        {
            if (value == null) issues.Add("Manager: " + name + " is not wired");
        }

        private static void CheckListener(List<string> issues, UnityEventBase eventBase,
            Component component, string propertyName, UdonSharpBehaviour behaviour, string eventName, string label)
        {
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            if (target == null) target = behaviour.GetComponent<VRC.Udon.UdonBehaviour>();
            if (eventBase.GetPersistentEventCount() != 1
                || eventBase.GetPersistentTarget(0) != target
                || eventBase.GetPersistentMethodName(0) != "SendCustomEvent")
            {
                issues.Add(label + ": Udon 永続リスナーが未配線です (" + eventName + ")");
                return;
            }
            var serialized = new SerializedObject(component);
            var argument = serialized.FindProperty(propertyName
                + ".m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
            if (argument == null || argument.stringValue != eventName)
                issues.Add(label + ": Udon イベント名が " + eventName + " ではありません");
        }

        private static GameObject FindSceneRoot(string name)
            => FindSceneRoot(SceneManager.GetActiveScene(), name);

        // Search the whole scene, preferring an existing child of the selected parent.
        public static GameObject FindSceneRoot(Scene scene, string name)
        {
            var parent = QvInfoX_SceneInstaller.FindInfrastructureParent(scene);
            var matches = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(item => item.name == name).ToArray();
            if (matches.Length > 1) Debug.LogWarning("Multiple '" + name + "' objects found; using one");
            var found = matches.FirstOrDefault(item => parent != null && item.parent == parent) ?? matches.FirstOrDefault();
            return found != null ? found.gameObject : null;
        }

        /// <summary>
        /// World Space Canvas がクリック/レーザーポインターを受け取るには、シーンに
        /// EventSystem (+ StandaloneInputModule) が存在している必要がある。無ければ用意する。
        /// </summary>
        private static void EnsureEventSystem()
        {
            var existing = FindSceneObjects<UnityEngine.EventSystems.EventSystem>(true);
            if (existing.Length > 0)
            {
                existing[0].gameObject.SetActive(true);
                if (existing[0].GetComponent<UnityEngine.EventSystems.StandaloneInputModule>() == null)
                    existing[0].gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");

            Debug.Log("[QvInfoX AutoSetup] シーンに EventSystem が無かったため作成しました (World Space UI の操作に必要)。");
        }

        #region Player Object Pool (CyanPlayerObjectPool)

        public const string PLAYER_POOL_ROOT_NAME = "QvInfoX_PlayerPool";
        // world player cap * 2 + 2 が CyanPlayerObjectPool 公式推奨値。個別ワールドの上限が
        // 分からない非侵襲ツールという性質上、余裕を持って標準の 82 (VRChat のインスタンス上限を
        // 十分にカバーできる値) を既定値として使う。
        public const int PLAYER_POOL_SIZE = 82;

        /// <summary>
        /// メインビュー (参加プレイヤーの読込状況一覧) がプレイヤーとスロットを結び付けるために必要な
        /// CyanPlayerObjectPool 一式をシーンに自動配置する。QvPen 本体同様、パッケージ本体
        /// (com.cyan.playerobjectpool) のスクリプト・Prefab は一切変更せず、公式コンポーネントを
        /// 追加するだけに留める。既に配置済みの場合は再生成せず、個数の不整合のみ補正する。
        /// </summary>
        private static void EnsurePlayerObjectPool()
        {
            var rootGo = FindSceneRoot(PLAYER_POOL_ROOT_NAME);
            var isNew = rootGo == null;
            if (isNew)
            {
                rootGo = new GameObject(PLAYER_POOL_ROOT_NAME);
                Undo.RegisterCreatedObjectUndo(rootGo, "Create QvInfoX Player Pool");
            }
            rootGo.SetActive(true);

            var pool = GetOrAddUdonSharpComponent<CyanPlayerObjectPool>(rootGo);
            if (pool.poolSize <= 0)
                pool.poolSize = PLAYER_POOL_SIZE;

            var assignerGo = GetOrCreateChild(rootGo.transform, "Assigner");
            var assigner = GetOrAddUdonSharpComponent<CyanPlayerObjectAssigner>(assignerGo);

            var slotsParentGo = GetOrCreateChild(assignerGo.transform, "PlayerSlots");
            assigner.poolObjectsParent = slotsParentGo.transform;

            // QvInfoX_PlayerSlot 自体が CyanPlayerObjectPoolObject (_OnOwnerSet/_OnCleanup) を実装しているため、
            // 別途 poolEventListener は不要。

            for (var i = slotsParentGo.transform.childCount; i < pool.poolSize; i++)
            {
                var slotGo = new GameObject($"PlayerSlot_{i}");
                slotGo.transform.SetParent(slotsParentGo.transform, false);
                GetOrAddUdonSharpComponent<QvInfoX_PlayerSlot>(slotGo);
                slotGo.SetActive(false);
            }

            // pool.poolSize を後から減らした場合等、余剰スロットは削除して整合させる。
            for (var i = slotsParentGo.transform.childCount - 1; i >= pool.poolSize; i--)
            {
                Undo.DestroyObjectImmediate(slotsParentGo.transform.GetChild(i).gameObject);
            }

            // A renamed or partially broken slot is reused and repaired in place.
            for (var i = 0; i < slotsParentGo.transform.childCount; i++)
            {
                var slotGo = slotsParentGo.transform.GetChild(i).gameObject;
                if (slotGo.GetComponent<QvInfoX_PlayerSlot>() == null)
                    GetOrAddUdonSharpComponent<QvInfoX_PlayerSlot>(slotGo);
                slotGo.SetActive(false);
            }

            SafeCopyProxyToUdon(pool);
            SafeCopyProxyToUdon(assigner);

            for (var i = 0; i < slotsParentGo.transform.childCount; i++)
            {
                var slot = slotsParentGo.transform.GetChild(i).GetComponent<QvInfoX_PlayerSlot>();
                if (slot != null)
                    SafeCopyProxyToUdon(slot);
            }

            if (isNew)
                Debug.Log($"[QvInfoX AutoSetup] CyanPlayerObjectPool 一式 (poolSize={pool.poolSize}) を作成しました。");
        }

        #endregion

        #region Program assets

        public static void EnsureAllProgramAssets()
        {
            EnsureProgramAsset(typeof(QvInfoX_PlayerSlot));
            EnsureProgramAsset(typeof(QvInfoX_Indicator));
            EnsureProgramAsset(typeof(QvInfoX_Manager));
            EnsureProgramAsset(typeof(QvInfoX_HoldController));
            EnsureProgramAsset(typeof(QvInfoX_WorldState));
            EnsureProgramAsset(typeof(QvInfoX_MasterTotals));
            AssetDatabase.SaveAssets();

            UdonSharpProgramAsset.CompileAllCsPrograms(true, true);
        }

        public static UdonSharpProgramAsset EnsureProgramAsset(Type scriptType)
        {
            var programAsset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(scriptType);
            if (programAsset != null)
            {
                if (programAsset.ScriptVersion < UdonSharpProgramVersion.CurrentVersion ||
                    programAsset.CompiledVersion < UdonSharpProgramVersion.CurrentVersion)
                {
                    programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                    programAsset.CompiledVersion = UdonSharpProgramVersion.CurrentVersion;
                    EditorUtility.SetDirty(programAsset);
                }
                return programAsset;
            }

            if (!Directory.Exists(UDON_ASSET_DIRECTORY))
            {
                Directory.CreateDirectory(UDON_ASSET_DIRECTORY);
                AssetDatabase.Refresh();
            }

            string scriptPath = null;
            var guids = AssetDatabase.FindAssets($"t:MonoScript {scriptType.Name}");
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(p);
                if (ms != null && ms.GetClass() == scriptType)
                {
                    scriptPath = p;
                    break;
                }
            }

            if (string.IsNullOrEmpty(scriptPath))
            {
                Debug.LogError($"[QvInfoX] MonoScript が見つかりません: {scriptType.FullName}");
                return null;
            }

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            var assetPath = $"{UDON_ASSET_DIRECTORY}/{scriptType.Name}.asset";

            programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = script;
            programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            programAsset.CompiledVersion = UdonSharpProgramVersion.CurrentVersion;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return programAsset;
        }

        /// <summary>
        /// UdonSharpProgramAsset.fieldDefinitions はコンパイル完了後にのみメモリ上へ構築される
        /// (アセットに永続化されない) 一時データのため、同一プロセス内でアセット生成直後に
        /// プロキシ同期を行うと未コンパイル状態のままとなり例外が発生することがある。
        /// その場合は致命的エラーとせず警告に留め、次回の Auto Setup 実行時に解決させる。
        /// </summary>
        private static void SafeCopyProxyToUdon(UdonSharpBehaviour proxy)
        {
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[QvInfoX AutoSetup] '{proxy.name}' の Udon への即時同期に失敗しました " +
                    "(UdonSharp のコンパイルが未完了の可能性があります)。" +
                    "Unity がスクリプトの再コンパイルを終えた後、もう一度 シーンに追加・更新 (Auto Setup) を実行してください。\n" + e.Message);
            }
        }

        public static T GetOrAddUdonSharpComponent<T>(GameObject go) where T : UdonSharpBehaviour
        {
            EnsureProgramAsset(typeof(T));

            var comp = go.GetComponent<T>();
            if (comp == null)
                comp = go.AddUdonSharpComponent<T>();

            return comp;
        }

        public static QvInfoX_WorldState SetupWorldState()
        {
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            QvInfoX_SceneInstaller.ValidateInfrastructureLayout(activeScene);
            var root = FindSceneRoot(WORLD_STATE_ROOT_NAME);
            if (root == null)
            {
                root = new GameObject(WORLD_STATE_ROOT_NAME);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, activeScene);
                Undo.RegisterCreatedObjectUndo(root, "Create QvInfoX WorldState");
            }
            root.SetActive(true);
            var state = GetOrAddUdonSharpComponent<QvInfoX_WorldState>(root);

            // Older setup runs or manual copies can leave extra state objects.
            // Keep the uniquely named infrastructure object and remove only extra QvInfoX state components.
            foreach (var other in FindSceneObjects<QvInfoX_WorldState>(true))
                if (other != state && other.gameObject.scene == activeScene)
                    Undo.DestroyObjectImmediate(other);
            SafeCopyProxyToUdon(state);
            EditorUtility.SetDirty(root);
            return state;
        }

        public static QvInfoX_MasterTotals SetupMasterTotals()
        {
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            QvInfoX_SceneInstaller.ValidateInfrastructureLayout(activeScene);
            var root = FindSceneRoot(MASTER_TOTALS_ROOT_NAME);
            if (root == null)
            {
                root = new GameObject(MASTER_TOTALS_ROOT_NAME);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, activeScene);
                Undo.RegisterCreatedObjectUndo(root, "Create QvInfoX MasterTotals");
            }
            root.SetActive(true);
            var totals = GetOrAddUdonSharpComponent<QvInfoX_MasterTotals>(root);
            foreach (var other in FindSceneObjects<QvInfoX_MasterTotals>(true))
                if (other != totals && other.gameObject.scene == activeScene)
                    Undo.DestroyObjectImmediate(other);
            SafeCopyProxyToUdon(totals);
            EditorUtility.SetDirty(root);
            return totals;
        }

        #endregion

        #region Indicator setup

        /// <summary>
        /// シーン内の全 QvPen_PenManager / QvPen_Pen を検出し、各ペンに Indicator を配置する。
        /// 既に配置済みの場合は二重生成せず、参照の再リンクのみを行う。
        /// </summary>
        public static List<QvInfoX_Indicator> SetupAllIndicators()
        {
            var result = new List<QvInfoX_Indicator>();

            var penManagers = FindSceneObjects<QvPen_PenManager>(true);
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var pm in penManagers)
            {
                if (pm == null || pm.gameObject.scene != activeScene)
                    continue;

                var pen = pm.GetComponentInChildren<QvPen_Pen>(true);
                if (pen == null)
                    continue;

                var indicator = SetupIndicatorForPen(pen, pm);
                if (indicator != null)
                    result.Add(indicator);
            }

            return result;
        }

        private static QvInfoX_Indicator SetupIndicatorForPen(QvPen_Pen pen, QvPen_PenManager penManager)
        {
            // 過去のバージョンでは Indicator をペンメッシュ (Pen/Mesh/...) の下へ再親付けしていたため、
            // 直下だけを見る pen.transform.Find では見つからず、二重生成の原因になっていた。
            // 階層のどこにあっても確実に検出し、複数見つかった場合は先頭以外を削除して自己修復する。
            var sceneIndicators = FindSceneObjects<QvInfoX_Indicator>(true);
            var matches = new List<QvInfoX_Indicator>();
            foreach (var candidate in sceneIndicators)
                if (candidate != null && candidate.gameObject.scene == pen.gameObject.scene
                    && (candidate.Pen == pen || candidate.transform.IsChildOf(pen.transform)))
                    matches.Add(candidate);
            var existingIndicators = matches.ToArray();
            GameObject indicatorGo;
            if (existingIndicators.Length > 0)
            {
                // pen.transform に最も近い (浅い) ものを正本として残す。
                var keepIndex = 0;
                var keepDepth = int.MaxValue;
                for (var i = 0; i < existingIndicators.Length; i++)
                {
                    var depth = 0;
                    var t = existingIndicators[i].transform;
                    while (t != null && t != pen.transform)
                    {
                        depth++;
                        t = t.parent;
                    }
                    if (depth < keepDepth)
                    {
                        keepDepth = depth;
                        keepIndex = i;
                    }
                }

                indicatorGo = existingIndicators[keepIndex].gameObject;

                for (var i = 0; i < existingIndicators.Length; i++)
                {
                    if (i == keepIndex || existingIndicators[i] == null)
                        continue;

                    var dupTransform = existingIndicators[i].transform;

                    // 正本が重複側の子孫になっている場合、そのまま破棄すると正本も
                    // 道連れで消えてしまう。その場合は破棄せず残す (親付け直しで解消する)。
                    if (indicatorGo.transform.IsChildOf(dupTransform))
                        continue;

                    Undo.DestroyObjectImmediate(dupTransform.gameObject);
                }

                // Update() の動的追従に一本化したので、古いバージョンが行っていた
                // ペンメッシュ配下への再親付けが残っていれば、ペン直下へ戻しておく。
                if (indicatorGo.transform.parent != pen.transform)
                    indicatorGo.transform.SetParent(pen.transform, false);
            }
            else
            {
                indicatorGo = new GameObject(INDICATOR_CHILD_NAME);
                indicatorGo.transform.SetParent(pen.transform, false);
            }

            var indicator = GetOrAddUdonSharpComponent<QvInfoX_Indicator>(indicatorGo);

            var legacyPreview = indicatorGo.transform.Find("Preview");
            if (legacyPreview != null)
            {
                // The old visual marks a scene which has not yet migrated its placement settings.
                foreach (var inkPreview in FindSceneObjects<QvInkPreview_Pen>(true))
                    if (inkPreview.Pen == pen)
                    {
                        inkPreview.tailDistance = indicator.tailDistance;
                        inkPreview.upOffset = indicator.upOffset;
                        inkPreview.tailAxis = (QvInkPreview_TailAxis)indicator.tailAxis;
                        inkPreview.customTailAxis = indicator.customTailAxis;
                        inkPreview.positionOffset = indicator.positionOffset;
                        UdonSharpEditorUtility.CopyProxyToUdon(inkPreview);
                        break;
                    }
                Undo.DestroyObjectImmediate(legacyPreview.gameObject);
            }

            // Current indicators are data-only. Migrate any surviving old UI colliders,
            // without changing the actual pen/pickup or collider-free indicator objects.
            if (indicatorGo.GetComponentsInChildren<Collider>(true).Length > 0)
                SetLayerRecursively(indicatorGo, WalkthroughLayer);

            var indicatorSo = new SerializedObject(indicator);
            indicatorSo.FindProperty("pen").objectReferenceValue = pen;
            indicatorSo.FindProperty("penManager").objectReferenceValue = penManager;
            indicatorSo.ApplyModifiedProperties();
            SafeCopyProxyToUdon(indicator);

            EditorUtility.SetDirty(indicatorGo);

            return indicator;
        }

        #endregion

        #region Monitor panel setup

        private static readonly Color PanelBackgroundColor = new Color(0.055f, 0.085f, 0.12f, 0.97f);
        private static readonly Color CardBackgroundColor = new Color(0.10f, 0.16f, 0.21f, 1f);
        private static readonly Color ControlBackgroundColor = new Color(0.15f, 0.22f, 0.28f, 1f);
        private static readonly Color AccentColor = new Color(0.29f, 0.83f, 0.78f, 1f);
        private static readonly Color ActiveTabColor = new Color(0.08f, 0.38f, 0.38f, 1f);
        private static readonly Color MutedTextColor = new Color(0.68f, 0.78f, 0.83f, 1f);
        private static readonly Color TextColor = new Color(0.95f, 0.97f, 0.98f, 1f);
        private static readonly Color HeaderTextColor = AccentColor;

        public static QvInfoX_Manager SetupMonitor(List<QvInfoX_Indicator> indicators,
            bool initiallyEnableHold = false, int holdInitialState = 0)
        {
            QvInfoX_SceneInstaller.ValidateInfrastructureLayout(SceneManager.GetActiveScene());
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                var manager = SetupMonitorPrepared(indicators, initiallyEnableHold, holdInitialState);
                Undo.CollapseUndoOperations(group);
                return manager;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static QvInfoX_Manager SetupMonitorPrepared(List<QvInfoX_Indicator> indicators,
            bool initiallyEnableHold, int holdInitialState)
        {
            var rootGo = FindSceneRoot(MONITOR_ROOT_NAME);
            var isNew = rootGo == null;

            if (isNew)
            {
                rootGo = new GameObject(MONITOR_ROOT_NAME);
                Undo.RegisterCreatedObjectUndo(rootGo, "Create QvInfoX Monitor");

                var descriptor = FindSceneObjects<VRCSceneDescriptor>(true).FirstOrDefault();
                var spawn = descriptor != null && descriptor.spawns != null && descriptor.spawns.Length > 0
                    ? descriptor.spawns[0] : null;
                var forward = spawn != null ? Vector3.ProjectOnPlane(spawn.forward, Vector3.up) : Vector3.forward;
                if (forward.sqrMagnitude < 0.000001f)
                    forward = Quaternion.Euler(0f, spawn.eulerAngles.y, 0f) * Vector3.forward;
                forward.Normalize();
                var origin = spawn != null ? spawn.position : Vector3.zero;
                rootGo.transform.position = origin + forward * 2f + Vector3.up * 0.9f;
                // A Unity UI Graphic faces local -Z; its +Z points away from the player.
                rootGo.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            rootGo.SetActive(true);

            // 旧バージョンで生成されていた別体ハンドルオブジェクトがあれば自己修復で削除
            var staleHandle = GameObject.Find(MONITOR_ROOT_NAME + "_Handle");
            if (staleHandle != null)
                Undo.DestroyObjectImmediate(staleHandle);

            // §3.1 物理挙動 / §7.4 Monitor の Hierarchy:
            // QvInfoX_Monitor (Root)
            //   ├─ [VRC_Pickup]   Kinematic
            //   ├─ [Rigidbody]    Is Kinematic = true
            //   ├─ [BoxCollider]  持ち手 (VRCPickup と同一 GameObject に配置してグラブ可能にする)
            //   └─ Canvas (World Space)
            var rb = rootGo.GetComponent<Rigidbody>();
            if (rb == null)
                rb = rootGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var pickup = rootGo.GetComponent<VRCPickup>();
            if (pickup == null)
                pickup = rootGo.AddComponent<VRCPickup>();
            pickup.InteractionText = "QvInfoX Monitor";
            pickup.UseText = "";
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
            pickup.proximity = 3.0f;
            pickup.pickupable = true;

            // VRChat の VRCPickup は、そのコンポーネント自身がアタッチされている GameObject に
            // Collider が存在しないと把持 (Grab) インタラクションが検出されない。
            // Canvas (y: -0.31m 〜 +0.31m) の下端の外側に BoxCollider を配置し、
            // ボタン操作と干渉することなく快適にピックアップできるようにする。
            var rootCol = rootGo.GetComponent<BoxCollider>();
            if (rootCol == null)
                rootCol = Undo.AddComponent<BoxCollider>(rootGo);
            Undo.RecordObject(rootCol, "Configure QvInfoX handle collider");
            rootCol.center = new Vector3(0f, -0.33f, 0f);
            rootCol.size = new Vector3(0.18f, 0.03f, 0.03f);
            rootCol.isTrigger = false;

            // パネル下端の外側に専用の持ち手バー (HandleBar) を構築。
            SetupHandleBar(rootGo.transform);

            var manager = GetOrAddUdonSharpComponent<QvInfoX_Manager>(rootGo);
            var holdController = QvInfoX_HoldSetup.SetupPrepared(rootGo.transform, out var holdSkippedCount,
                initiallyEnableHold, holdInitialState);
            SafeCopyProxyToUdon(holdController);
            if (!manager.initialPoseCaptured)
            {
                manager.initialLocalPosition = rootGo.transform.localPosition;
                manager.initialLocalRotation = rootGo.transform.localRotation;
                manager.initialPoseCaptured = true;
            }
            // Migrate monitors created with the old default while preserving custom intervals.
            if (Mathf.Approximately(manager.autoUpdateInterval, 60f))
                manager.autoUpdateInterval = 10f;

            // Canvas (World Space)。420x620 (UI units) x 0.001 scale = 実寸 42cm x 62cm 程度の手持ちパネル。
            var canvasGo = GetOrCreateChild(rootGo.transform, "Canvas");
            var canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null)
                canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(420f, 620f);
            canvasGo.transform.localPosition = Vector3.zero;
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * 0.001f;

            var raycaster = canvasGo.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
                raycaster = canvasGo.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;

            if (canvasGo.GetComponent<CanvasScaler>() == null)
                canvasGo.AddComponent<CanvasScaler>();

            // VRChat は World Space Canvas を "VRCUiShape" 付きのものだけ、Desktop のマウス操作や
            // VR のレーザーポインターの対象として扱う。GraphicRaycaster だけでは
            // クリック/レーザーが一切届かず、パネルの操作が全くできなくなる。
            if (canvasGo.GetComponent<VRCUiShape>() == null)
                canvasGo.AddComponent<VRCUiShape>();

            // VRChat の入力判定は VRCUiShape のみでは完結せず、実際には Physics.Raycast で
            // "VRCUiShape が付いた GameObject 自身の Collider" に当たって初めて成立する
            // (ClientSimRaycaster.CheckForInteracts と同じ経路)。Collider が無いとレイが
            // そもそも当たらず、ボタンはおろかパネルの操作が一切できなくなる。
            var canvasCollider = canvasGo.GetComponent<BoxCollider>();
            if (canvasCollider == null)
                canvasCollider = canvasGo.AddComponent<BoxCollider>();
            canvasCollider.size = new Vector3(canvasRect.sizeDelta.x, canvasRect.sizeDelta.y, 1f);
            canvasCollider.center = Vector3.zero;
            canvasCollider.isTrigger = true;

            // Panel: キャンバス全体を覆う背景。
            // 子要素 (Header / Tabs / ScrollArea / Footer) は VerticalLayoutGroup ではなく、
            // 明示的なアンカー計算で配置する。Text や入れ子の LayoutGroup 自身も ILayoutElement を
            // 実装するため、同じ GameObject に LayoutElement を足すだけでは優先順位が不定になり、
            // 意図したサイズにならないことがある (実測で確認済み)。固定領域はアンカー分割にして
            // この問題自体を起こさないようにする。
            var panelGo = GetOrCreateChild(canvasGo.transform, "Panel");
            StretchFill(panelGo.GetComponent<RectTransform>());

            // 旧バージョンは Canvas 直下に MainView / DetailView / Tabs / Footer を置いていた。現行は Panel 配下へ
            // 作り直すため、残っているとパネル中央に四角い残骸として透けて見える。自己修復で削除する。
            foreach (var legacyName in new[] { "MainView", "DetailView", "Tabs", "Footer" })
            {
                var legacy = canvasGo.transform.Find(legacyName);
                if (legacy != null)
                    Undo.DestroyObjectImmediate(legacy.gameObject);
            }
            var panelImage = EnsureImage(panelGo, PanelBackgroundColor);
            panelImage.raycastTarget = false;

            const float padding = 16f;
            const float spacing = 8f;
            const float headerHeight = 88f;
            const float tabsHeight = 48f;
            const float footerHeight = 172f;

            // The bottom margin stays clear of the footer controls and the centered handle.
            var versionTransform = panelGo.transform.Find("VersionLabel");
            GameObject versionGo;
            if (versionTransform == null)
            {
                versionGo = new GameObject("VersionLabel", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(versionGo, "Create QvInfoX version label");
                Undo.SetTransformParent(versionGo.transform, panelGo.transform, "Parent QvInfoX version label");
            }
            else
                versionGo = versionTransform.gameObject;

            var versionRect = versionGo.GetComponent<RectTransform>();
            Undo.RecordObject(versionRect, "Position QvInfoX version label");
            versionRect.anchorMin = new Vector2(1f, 0f);
            versionRect.anchorMax = new Vector2(1f, 0f);
            versionRect.pivot = new Vector2(1f, 0f);
            versionRect.sizeDelta = new Vector2(150f, 14f);
            versionRect.localRotation = Quaternion.identity;
            versionRect.localScale = Vector3.one;
            versionRect.anchoredPosition3D = new Vector3(-16f, 1f, 0f);
            if (versionGo.GetComponent<Text>() == null)
                Undo.AddComponent<Text>(versionGo);
            Undo.RecordObject(versionGo.GetComponent<Text>(), "Update QvInfoX version label");
            var versionText = EnsureText(versionGo, 12, TextAnchor.MiddleRight, MutedTextColor);
            var packageVersion = ReadPackageVersion();
            versionText.text = string.IsNullOrEmpty(packageVersion) ? "" : "QvInfoX v" + packageVersion;

            // 持ち手の見た目は Canvas の外に置き、Root の VRC_Pickup で移動する。

            // Title and live totals have separate lines so changing numbers cannot overlap the tabs.
            var headerGo = GetOrCreateChild(panelGo.transform, "Header_Top3Text");
            AnchorTopBand(headerGo.GetComponent<RectTransform>(), padding, padding, headerHeight);
            var titleGo = GetOrCreateChild(headerGo.transform, "Title");
            AnchorTopBand(titleGo.GetComponent<RectTransform>(), 0f, 0f, 28f);
            var titleText = EnsureText(titleGo, 22, TextAnchor.MiddleLeft, HeaderTextColor);
            titleText.text = "QvInfoX  /  SYNC MONITOR";

            var totalsGo = GetOrCreateChild(headerGo.transform, "Totals");
            AnchorBottomBand(totalsGo.GetComponent<RectTransform>(), 0f, 0f, 52f);
            var top3Text = EnsureText(totalsGo, 15, TextAnchor.MiddleLeft);
            top3Text.lineSpacing = 1.15f;
            var oldHeaderText = headerGo.GetComponent<Text>();
            if (oldHeaderText != null)
                Undo.DestroyObjectImmediate(oldHeaderText);

            // タブ切替ボタン (ヘッダーの下、上端固定)
            var tabsGo = GetOrCreateChild(panelGo.transform, "Tabs");
            AnchorTopBand(tabsGo.GetComponent<RectTransform>(), padding, padding + headerHeight + spacing, tabsHeight);
            var tabsLayout = tabsGo.GetComponent<HorizontalLayoutGroup>();
            if (tabsLayout == null)
                tabsLayout = tabsGo.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.spacing = 8f;
            tabsLayout.childControlWidth = true;
            tabsLayout.childControlHeight = true;
            tabsLayout.childForceExpandWidth = true;
            tabsLayout.childForceExpandHeight = true;

            var mainTabGo = GetOrCreateChild(tabsGo.transform, "MainTabButton");
            var mainTabButton = EnsureButton(mainTabGo, LanguageText(manager.language, "プレイヤー", "Players", "플레이어"));
            var detailTabGo = GetOrCreateChild(tabsGo.transform, "DetailTabButton");
            var detailTabButton = EnsureButton(detailTabGo, LanguageText(manager.language, "ペン詳細", "Pens", "펜 상세"));
            mainTabButton.GetComponent<Image>().color = ActiveTabColor;

            // フッター (下端固定)
            var footerGo = GetOrCreateChild(panelGo.transform, "Footer");
            AnchorBottomBand(footerGo.GetComponent<RectTransform>(), padding, padding, footerHeight);

            // スクロール領域: ヘッダー/タブとフッターの間を正確に埋める (メインビューと詳細ビューを重ねて配置)。
            var scrollAreaGo = GetOrCreateChild(panelGo.transform, "ScrollArea");
            var scrollAreaRect = scrollAreaGo.GetComponent<RectTransform>();
            scrollAreaRect.anchorMin = Vector2.zero;
            scrollAreaRect.anchorMax = Vector2.one;
            scrollAreaRect.pivot = new Vector2(0.5f, 0.5f);
            scrollAreaRect.offsetMin = new Vector2(padding, padding + footerHeight + spacing);
            scrollAreaRect.offsetMax = new Vector2(-padding, -(padding + headerHeight + spacing + tabsHeight + spacing));

            var mainView = GetOrCreateChild(scrollAreaGo.transform, "MainView");
            StretchFill(mainView.GetComponent<RectTransform>());
            var mainScrollRect = BuildScrollRect(mainView, out var rowsContainerGo);

            var rowsLayout = rowsContainerGo.GetComponent<VerticalLayoutGroup>();
            if (rowsLayout == null)
                rowsLayout = rowsContainerGo.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 6f;
            rowsLayout.padding = new RectOffset(2, 2, 4, 4);
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;

            var rowsFitter = rowsContainerGo.GetComponent<ContentSizeFitter>();
            if (rowsFitter == null)
                rowsFitter = rowsContainerGo.AddComponent<ContentSizeFitter>();
            rowsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // §3.3: status ring, name/status, and a separate large percent with authored count.
            const int ROW_POOL_SIZE = PLAYER_POOL_SIZE;
            var ringSprite = GetOrGenerateRingSprite();
            var rowTexts = new List<Text>();
            var rowMetaTexts = new List<Text>();
            var rowAuthoredTexts = new List<Text>();
            var rowProgressBars = new List<Image>();
            var rowPercentTexts = new List<Text>();
            var poolTrans = rowsContainerGo.transform;
            for (var i = 0; i < ROW_POOL_SIZE; i++)
            {
                var rowName = $"Row_{i}";
                var rowTrans = poolTrans.Find(rowName);
                GameObject rowGo = rowTrans != null ? rowTrans.gameObject : new GameObject(rowName);
                if (rowTrans == null)
                    rowGo.transform.SetParent(poolTrans, false);

                // 旧バージョン (Text 直付け) の残骸を除去し、行自体は HorizontalLayoutGroup に作り直す。
                var staleRowText = rowGo.GetComponent<Text>();
                if (staleRowText != null)
                    Undo.DestroyObjectImmediate(staleRowText);

                var rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                if (rowLayout == null)
                    rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 10f;
                rowLayout.padding = new RectOffset(10, 10, 0, 0);
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = false;
                SetLayoutElement(rowGo, preferredHeight: 68f);
                EnsureImage(rowGo, CardBackgroundColor).raycastTarget = false;

                // Remove the old progress bar before rebuilding the ring.
                var staleProgressBar = rowGo.transform.Find("ProgressFrame/ProgressBar");
                if (staleProgressBar != null)
                    Undo.DestroyObjectImmediate(staleProgressBar.gameObject);

                var progressFrameGo = GetOrCreateChild(rowGo.transform, "ProgressFrame");
                SetLayoutElement(progressFrameGo, preferredWidth: 38f, preferredHeight: 38f);

                var ringBgGo = GetOrCreateChild(progressFrameGo.transform, "RingBackground");
                StretchFill(ringBgGo.GetComponent<RectTransform>());
                var ringBgImage = EnsureImage(ringBgGo, ControlBackgroundColor);
                ringBgImage.sprite = ringSprite;
                ringBgImage.type = Image.Type.Simple;
                ringBgImage.raycastTarget = false;

                var ringFillGo = GetOrCreateChild(progressFrameGo.transform, "RingFill");
                StretchFill(ringFillGo.GetComponent<RectTransform>());
                var ringFillImage = EnsureImage(ringFillGo, AccentColor);
                ringFillImage.sprite = ringSprite;
                ringFillImage.type = Image.Type.Filled;
                ringFillImage.fillMethod = Image.FillMethod.Radial360;
                ringFillImage.fillOrigin = (int)Image.Origin360.Top;
                ringFillImage.fillClockwise = true;
                ringFillImage.fillAmount = 0f;
                ringFillImage.raycastTarget = false;

                var staleRingText = progressFrameGo.transform.Find("RingPercentText");
                if (staleRingText != null)
                    Undo.DestroyObjectImmediate(staleRingText.gameObject);

                var labelGroupGo = GetOrCreateChild(rowGo.transform, "LabelGroup");
                SetLayoutElement(labelGroupGo, flexibleWidth: 1f, preferredHeight: 68f);
                var nameGo = GetOrCreateChild(labelGroupGo.transform, "Name");
                AnchorTopBand(nameGo.GetComponent<RectTransform>(), 0f, 9f, 25f);
                var nameText = EnsureText(nameGo, 17, TextAnchor.MiddleLeft);
                nameText.verticalOverflow = VerticalWrapMode.Truncate;
                nameText.resizeTextForBestFit = true;
                nameText.resizeTextMinSize = 13;
                nameText.resizeTextMaxSize = 17;

                var metaGo = GetOrCreateChild(labelGroupGo.transform, "Meta");
                AnchorBottomBand(metaGo.GetComponent<RectTransform>(), 0f, 9f, 18f);
                var metaText = EnsureText(metaGo, 12, TextAnchor.MiddleLeft, MutedTextColor);
                metaText.verticalOverflow = VerticalWrapMode.Truncate;

                var oldLabel = rowGo.transform.Find("Label");
                if (oldLabel != null)
                    Undo.DestroyObjectImmediate(oldLabel.gameObject);

                var percentGroupGo = GetOrCreateChild(rowGo.transform, "PercentGroup");
                SetLayoutElement(percentGroupGo, preferredWidth: 60f, preferredHeight: 68f);
                var percentGo = GetOrCreateChild(percentGroupGo.transform, "PercentText");
                AnchorTopBand(percentGo.GetComponent<RectTransform>(), 0f, 9f, 30f);
                var percentText = EnsureText(percentGo, 19, TextAnchor.MiddleRight);
                percentText.horizontalOverflow = HorizontalWrapMode.Overflow;
                percentText.text = "—";
                var authoredGo = GetOrCreateChild(percentGroupGo.transform, "AuthoredText");
                AnchorBottomBand(authoredGo.GetComponent<RectTransform>(), 0f, 9f, 18f);
                var authoredText = EnsureText(authoredGo, 11, TextAnchor.MiddleRight, MutedTextColor);
                authoredText.resizeTextForBestFit = true;
                authoredText.resizeTextMinSize = 9;
                authoredText.resizeTextMaxSize = 11;
                var oldPercent = rowGo.transform.Find("PercentText");
                if (oldPercent != null)
                    Undo.DestroyObjectImmediate(oldPercent.gameObject);

                rowGo.SetActive(false);
                rowTexts.Add(nameText);
                rowMetaTexts.Add(metaText);
                rowAuthoredTexts.Add(authoredText);
                rowProgressBars.Add(ringFillImage);
                rowPercentTexts.Add(percentText);
            }

            var detailView = GetOrCreateChild(scrollAreaGo.transform, "DetailView");
            StretchFill(detailView.GetComponent<RectTransform>());
            var detailScrollRect = BuildScrollRect(detailView, out var detailContentGo);

            var detailRowsLayout = detailContentGo.GetComponent<VerticalLayoutGroup>();
            if (detailRowsLayout == null)
                detailRowsLayout = detailContentGo.AddComponent<VerticalLayoutGroup>();
            detailRowsLayout.spacing = 6f;
            detailRowsLayout.padding = new RectOffset(2, 2, 4, 4);
            detailRowsLayout.childControlWidth = true;
            detailRowsLayout.childControlHeight = true;
            detailRowsLayout.childForceExpandWidth = true;
            detailRowsLayout.childForceExpandHeight = false;

            var detailFitter = detailContentGo.GetComponent<ContentSizeFitter>();
            if (detailFitter == null)
                detailFitter = detailContentGo.AddComponent<ContentSizeFitter>();
            detailFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // §5.4 / §7.4: ink preview, two-line pen summary, progress rail, and separate percent.
            const int DETAIL_ROW_POOL_SIZE = 40;
            var detailInkLines = new List<RawImage>();
            var detailProgressBars = new List<Image>();
            var detailPercentTexts = new List<Text>();
            var detailRowTexts = new List<Text>();
            var detailMetaTexts = new List<Text>();
            for (var i = 0; i < DETAIL_ROW_POOL_SIZE; i++)
            {
                var rowName = $"DetailRow_{i}";
                var rowTrans = detailContentGo.transform.Find(rowName);
                var detailRowGo = rowTrans != null ? rowTrans.gameObject : new GameObject(rowName);
                if (rowTrans == null)
                    detailRowGo.transform.SetParent(detailContentGo.transform, false);

                // 過去バージョンで生成された単色スワッチの残骸があれば削除する (廃止済み)。
                var staleSwatchFrame = detailRowGo.transform.Find("SwatchFrame");
                if (staleSwatchFrame != null)
                    Undo.DestroyObjectImmediate(staleSwatchFrame.gameObject);

                var detailRowLayout = detailRowGo.GetComponent<HorizontalLayoutGroup>();
                if (detailRowLayout == null)
                    detailRowLayout = detailRowGo.AddComponent<HorizontalLayoutGroup>();
                detailRowLayout.spacing = 10f;
                detailRowLayout.padding = new RectOffset(10, 10, 0, 0);
                detailRowLayout.childAlignment = TextAnchor.MiddleLeft;
                detailRowLayout.childControlWidth = true;
                detailRowLayout.childControlHeight = true;
                detailRowLayout.childForceExpandWidth = false;
                detailRowLayout.childForceExpandHeight = false;
                SetLayoutElement(detailRowGo, preferredHeight: 72f);
                EnsureImage(detailRowGo, CardBackgroundColor).raycastTarget = false;

                // 対応するペンのインクプレビューテクスチャ (虹色・グラデーション・特殊マテリアル・Alpha忠実再現) を取得
                // ランタイムの行順 (DiscoverPens: シーン階層順) は AutoSetup のこの列挙順 (FindObjectsOfType)
                // と一致しない場合があるため、テクスチャは行 index ではなく Indicator (= Pen) 側にも
                // 保存しておき、Manager 側で Pen 参照から引き当てられるようにする。
                Texture2D inkTex = null;
                if (i < indicators.Count && indicators[i] != null && indicators[i].PenManager != null)
                {
                    inkTex = GetOrGenerateInkPreviewTexture(indicators[i].PenManager, i);

                    var indicatorSo = new SerializedObject(indicators[i]);
                    var texProp = indicatorSo.FindProperty("inkPreviewTexture");
                    if (texProp != null && texProp.objectReferenceValue != inkTex)
                    {
                        texProp.objectReferenceValue = inkTex;
                        indicatorSo.ApplyModifiedProperties();
                    }
                }

                // 太さプレビューバー (§5.4: 実際の線幅プレビュー + 本物のインクグラデーション)
                // ペン判別はこのインクプレビュー1つで十分とのフィードバックにより、単色スウォッチは廃止した。
                var inkFrameGo = GetOrCreateChild(detailRowGo.transform, "InkPreviewFrame");
                EnsureImage(inkFrameGo, new Color(0.12f, 0.14f, 0.18f, 0.9f)).raycastTarget = false;
                SetLayoutElement(inkFrameGo, preferredWidth: 56f, preferredHeight: 28f);

                var inkLineGo = GetOrCreateChild(inkFrameGo.transform, "InkLine");
                var inkLineRt = inkLineGo.GetComponent<RectTransform>();
                inkLineRt.anchorMin = new Vector2(0.5f, 0.5f);
                inkLineRt.anchorMax = new Vector2(0.5f, 0.5f);
                inkLineRt.pivot = new Vector2(0.5f, 0.5f);
                inkLineRt.sizeDelta = new Vector2(50f, 4f);
                var inkLineImage = EnsureRawImage(inkLineGo, inkTex, Color.white);
                inkLineImage.raycastTarget = false;

                // Keep the pen preview and a short progress rail; the percent is large and separate.
                var oldProgressFrame = detailRowGo.transform.Find("ProgressFrame");
                if (oldProgressFrame != null)
                    Undo.DestroyObjectImmediate(oldProgressFrame.gameObject);

                var detailLabelGroupGo = GetOrCreateChild(detailRowGo.transform, "LabelGroup");
                SetLayoutElement(detailLabelGroupGo, flexibleWidth: 1f, preferredHeight: 72f);
                var detailNameGo = GetOrCreateChild(detailLabelGroupGo.transform, "Name");
                AnchorTopBand(detailNameGo.GetComponent<RectTransform>(), 0f, 7f, 26f);
                var detailNameText = EnsureText(detailNameGo, 16, TextAnchor.MiddleLeft);
                detailNameText.verticalOverflow = VerticalWrapMode.Truncate;
                detailNameText.resizeTextForBestFit = true;
                detailNameText.resizeTextMinSize = 12;
                detailNameText.resizeTextMaxSize = 16;

                var detailMetaGo = GetOrCreateChild(detailLabelGroupGo.transform, "Meta");
                AnchorBottomBand(detailMetaGo.GetComponent<RectTransform>(), 0f, 17f, 18f);
                var detailMetaText = EnsureText(detailMetaGo, 12, TextAnchor.MiddleLeft, MutedTextColor);
                detailMetaText.verticalOverflow = VerticalWrapMode.Truncate;

                var progressTrackGo = GetOrCreateChild(detailLabelGroupGo.transform, "ProgressTrack");
                AnchorBottomBand(progressTrackGo.GetComponent<RectTransform>(), 0f, 8f, 4f);
                EnsureImage(progressTrackGo, ControlBackgroundColor).raycastTarget = false;
                var progressFillGo = GetOrCreateChild(progressTrackGo.transform, "Fill");
                StretchFill(progressFillGo.GetComponent<RectTransform>());
                progressFillGo.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
                var progressBarImage = EnsureImage(progressFillGo, AccentColor);
                progressBarImage.type = Image.Type.Simple;
                progressBarImage.raycastTarget = false;

                var oldDetailLabel = detailRowGo.transform.Find("Label");
                if (oldDetailLabel != null)
                    Undo.DestroyObjectImmediate(oldDetailLabel.gameObject);
                var detailPercentGo = GetOrCreateChild(detailRowGo.transform, "PercentText");
                SetLayoutElement(detailPercentGo, preferredWidth: 56f, preferredHeight: 72f);
                var detailPercentText = EnsureText(detailPercentGo, 19, TextAnchor.MiddleRight);
                detailPercentText.horizontalOverflow = HorizontalWrapMode.Overflow;
                detailPercentText.text = "—";

                detailRowGo.SetActive(false);
                detailInkLines.Add(inkLineImage);
                detailProgressBars.Add(progressBarImage);
                detailPercentTexts.Add(detailPercentText);
                detailRowTexts.Add(detailNameText);
                detailMetaTexts.Add(detailMetaText);
            }

            // タブの既定状態はメインビュー (要望対応: 以前は編集時に詳細タブがアクティブなまま
            // 保存されると、ワールド読み込み直後から詳細ビューが表示されてしまっていた)。
            mainView.SetActive(true);
            detailView.SetActive(false);

            // Separate status lines and two control rows, shared by both tabs.
            var footerLayout = footerGo.GetComponent<HorizontalLayoutGroup>();
            if (footerLayout != null)
                Undo.DestroyObjectImmediate(footerLayout);
            EnsureImage(footerGo, CardBackgroundColor).raycastTarget = false;

            var manualButtonGo = GetOrCreateChild(footerGo.transform, "ManualUpdateButton");
            var manualButtonRect = manualButtonGo.GetComponent<RectTransform>();
            manualButtonRect.anchorMin = new Vector2(0f, 0f);
            manualButtonRect.anchorMax = new Vector2(0f, 0f);
            manualButtonRect.pivot = new Vector2(0f, 0f);
            manualButtonRect.anchoredPosition = new Vector2(10f, 9f);
            manualButtonRect.sizeDelta = new Vector2(96f, 44f);
            var manualButton = EnsureButton(manualButtonGo,
                LanguageText(manager.language, "今すぐ更新", "Refresh", "새로 고침"));

            var resetGo = GetOrCreateChild(footerGo.transform, "ResetPositionButton");
            var resetRect = resetGo.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(0f, 0f);
            resetRect.anchorMax = new Vector2(0f, 0f);
            resetRect.pivot = new Vector2(0f, 0f);
            resetRect.anchoredPosition = new Vector2(112f, 9f);
            resetRect.sizeDelta = new Vector2(96f, 44f);
            var resetButton = EnsureButton(resetGo,
                LanguageText(manager.language, "位置戻す", "Reset", "위치 복원"));

            var languageGo = GetOrCreateChild(footerGo.transform, "LanguageButton");
            var languageRect = languageGo.GetComponent<RectTransform>();
            languageRect.anchorMin = new Vector2(1f, 0f);
            languageRect.anchorMax = new Vector2(1f, 0f);
            languageRect.pivot = new Vector2(1f, 0f);
            languageRect.anchoredPosition = new Vector2(-10f, 9f);
            languageRect.sizeDelta = new Vector2(70f, 44f);
            var languageButton = EnsureButton(languageGo,
                manager.language == 1 ? "EN" : manager.language == 2 ? "한국어" : "日本語");

            var footerStatusGo = GetOrCreateChild(footerGo.transform, "StatusText");
            AnchorTopBand(footerStatusGo.GetComponent<RectTransform>(), 10f, 7f, 24f);
            var footerStatusText = EnsureText(footerStatusGo, 13, TextAnchor.MiddleLeft, MutedTextColor);

            var toggleGo = GetOrCreateChild(footerGo.transform, "IndicatorToggle");
            var toggleRect = toggleGo.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(0f, 0f);
            toggleRect.anchorMax = new Vector2(0f, 0f);
            toggleRect.pivot = new Vector2(0f, 0f);
            toggleRect.anchoredPosition = new Vector2(10f, 61f);
            toggleRect.sizeDelta = new Vector2(160f, 44f);
            var indicatorToggle = EnsureToggle(toggleGo,
                LanguageText(manager.language, "ペン表示", "Show pens", "펜 표시"));

            var holdGo = GetOrCreateChild(footerGo.transform, "HoldToggle");
            var holdRect = holdGo.GetComponent<RectTransform>();
            holdRect.anchorMin = new Vector2(1f, 0f);
            holdRect.anchorMax = new Vector2(1f, 0f);
            holdRect.pivot = new Vector2(1f, 0f);
            holdRect.anchoredPosition = new Vector2(-10f, 61f);
            holdRect.sizeDelta = new Vector2(180f, 44f);
            var holdToggle = EnsureToggle(holdGo,
                LanguageText(manager.language, "ペン保持", "Hold pens", "펜 유지"),
                holdController.requestedEnabled);
            holdToggle.interactable = holdController.targetPens.Length > 0;
            var holdStatusGo = GetOrCreateChild(footerGo.transform, "HoldStatusText");
            AnchorBottomBand(holdStatusGo.GetComponent<RectTransform>(), 10f, 113f, 20f);
            var holdStatusText = EnsureText(holdStatusGo, 13, TextAnchor.MiddleLeft, MutedTextColor);
            holdStatusText.text = holdToggle.interactable
                ? LanguageText(manager.language, "QvHold・自分のみ", "QvHold · Local only", "QvHold · 나에게만 적용")
                : LanguageText(manager.language, "対応するペンなし", "No compatible pens", "호환 펜 없음");

            // イベント配線 (VR / In-Game で確実に Udon VM へ届くよう UdonBehaviour.SendCustomEvent にバインド)
            WireButtonToUdon(manualButton, manager, nameof(QvInfoX_Manager._OnManualUpdateButton));
            WireButtonToUdon(resetButton, manager, nameof(QvInfoX_Manager._ResetPosition));
            WireButtonToUdon(mainTabButton, manager, nameof(QvInfoX_Manager._ShowMainView));
            WireButtonToUdon(detailTabButton, manager, nameof(QvInfoX_Manager._ShowDetailView));
            WireButtonToUdon(languageButton, manager, nameof(QvInfoX_Manager._CycleLanguage));
            WireToggleToUdon(indicatorToggle, manager, nameof(QvInfoX_Manager._OnIndicatorToggleChanged));
            WireToggleToUdon(holdToggle, manager, nameof(QvInfoX_Manager._OnHoldToggleChanged));

            // 非インタラクティブ Graphic の Raycast Target を一括 OFF (ボタン・トグル以外)
            DisableNonInteractiveRaycastTargets(canvasGo);

            // Manager 参照配線
            var managerSo = new SerializedObject(manager);
            var indicatorsProp = managerSo.FindProperty("indicators");
            indicatorsProp.arraySize = indicators.Count;
            for (var i = 0; i < indicators.Count; i++)
                indicatorsProp.GetArrayElementAtIndex(i).objectReferenceValue = indicators[i];

            var rowPoolProp = managerSo.FindProperty("rowPool");
            rowPoolProp.arraySize = rowTexts.Count;
            for (var i = 0; i < rowTexts.Count; i++)
                rowPoolProp.GetArrayElementAtIndex(i).objectReferenceValue = rowTexts[i];

            var rowMetaTextsProp = managerSo.FindProperty("rowMetaTexts");
            rowMetaTextsProp.arraySize = rowMetaTexts.Count;
            for (var i = 0; i < rowMetaTexts.Count; i++)
                rowMetaTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = rowMetaTexts[i];

            var rowAuthoredTextsProp = managerSo.FindProperty("rowAuthoredTexts");
            rowAuthoredTextsProp.arraySize = rowAuthoredTexts.Count;
            for (var i = 0; i < rowAuthoredTexts.Count; i++)
                rowAuthoredTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = rowAuthoredTexts[i];

            var rowProgressBarsProp = managerSo.FindProperty("rowProgressBars");
            rowProgressBarsProp.arraySize = rowProgressBars.Count;
            for (var i = 0; i < rowProgressBars.Count; i++)
                rowProgressBarsProp.GetArrayElementAtIndex(i).objectReferenceValue = rowProgressBars[i];

            var rowPercentTextsProp = managerSo.FindProperty("rowPercentTexts");
            rowPercentTextsProp.arraySize = rowPercentTexts.Count;
            for (var i = 0; i < rowPercentTexts.Count; i++)
                rowPercentTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = rowPercentTexts[i];

            managerSo.FindProperty("rowsContainer").objectReferenceValue = rowsContainerGo.transform;
            managerSo.FindProperty("mainScrollRect").objectReferenceValue = mainScrollRect;
            managerSo.FindProperty("top3Text").objectReferenceValue = top3Text;
            managerSo.FindProperty("manualUpdateButton").objectReferenceValue = manualButton;
            managerSo.FindProperty("resetPositionButton").objectReferenceValue = resetButton;
            managerSo.FindProperty("resetPositionButtonText").objectReferenceValue = resetGo.transform.Find("Label").GetComponent<Text>();
            managerSo.FindProperty("footerStatusText").objectReferenceValue = footerStatusText;
            managerSo.FindProperty("indicatorToggle").objectReferenceValue = indicatorToggle;
            managerSo.FindProperty("holdController").objectReferenceValue = holdController;
            managerSo.FindProperty("holdToggle").objectReferenceValue = holdToggle;
            managerSo.FindProperty("holdToggleText").objectReferenceValue = holdGo.transform.Find("Label").GetComponent<Text>();
            managerSo.FindProperty("holdStatusText").objectReferenceValue = holdStatusText;
            managerSo.FindProperty("languageButton").objectReferenceValue = languageButton;
            managerSo.FindProperty("languageButtonText").objectReferenceValue = languageGo.transform.Find("Label").GetComponent<Text>();
            managerSo.FindProperty("manualButtonText").objectReferenceValue = manualButtonGo.transform.Find("Label").GetComponent<Text>();
            managerSo.FindProperty("indicatorToggleText").objectReferenceValue = toggleGo.transform.Find("Label").GetComponent<Text>();
            managerSo.FindProperty("mainTabText").objectReferenceValue = mainTabGo.transform.Find("Label").GetComponent<Text>();
            managerSo.FindProperty("detailTabText").objectReferenceValue = detailTabGo.transform.Find("Label").GetComponent<Text>();
            managerSo.FindProperty("mainViewRoot").objectReferenceValue = mainView;
            managerSo.FindProperty("detailViewRoot").objectReferenceValue = detailView;
            managerSo.FindProperty("detailScrollRect").objectReferenceValue = detailScrollRect;
            managerSo.FindProperty("mainTabImage").objectReferenceValue = mainTabButton.GetComponent<Image>();
            managerSo.FindProperty("detailTabImage").objectReferenceValue = detailTabButton.GetComponent<Image>();

            var detailInkLinesProp = managerSo.FindProperty("detailInkLines");
            detailInkLinesProp.arraySize = detailInkLines.Count;
            for (var i = 0; i < detailInkLines.Count; i++)
                detailInkLinesProp.GetArrayElementAtIndex(i).objectReferenceValue = detailInkLines[i];

            var detailProgressBarsProp = managerSo.FindProperty("detailProgressBars");
            detailProgressBarsProp.arraySize = detailProgressBars.Count;
            for (var i = 0; i < detailProgressBars.Count; i++)
                detailProgressBarsProp.GetArrayElementAtIndex(i).objectReferenceValue = detailProgressBars[i];

            var detailPercentTextsProp = managerSo.FindProperty("detailPercentTexts");
            detailPercentTextsProp.arraySize = detailPercentTexts.Count;
            for (var i = 0; i < detailPercentTexts.Count; i++)
                detailPercentTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = detailPercentTexts[i];

            var detailRowTextsProp = managerSo.FindProperty("detailRowTexts");
            detailRowTextsProp.arraySize = detailRowTexts.Count;
            for (var i = 0; i < detailRowTexts.Count; i++)
                detailRowTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = detailRowTexts[i];

            var detailMetaTextsProp = managerSo.FindProperty("detailMetaTexts");
            detailMetaTextsProp.arraySize = detailMetaTexts.Count;
            for (var i = 0; i < detailMetaTexts.Count; i++)
                detailMetaTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = detailMetaTexts[i];

            // デスクトップ用の直接ヒットテスト対象 (QvPalette の _PollDesktopInteraction と同じ方式)。
            managerSo.FindProperty("manualUpdateButtonRect").objectReferenceValue = manualButtonGo.GetComponent<RectTransform>();
            managerSo.FindProperty("mainTabRect").objectReferenceValue = mainTabGo.GetComponent<RectTransform>();
            managerSo.FindProperty("detailTabRect").objectReferenceValue = detailTabGo.GetComponent<RectTransform>();
            managerSo.FindProperty("indicatorToggleRect").objectReferenceValue = toggleGo.GetComponent<RectTransform>();
            managerSo.FindProperty("holdToggleRect").objectReferenceValue = holdRect;
            managerSo.FindProperty("languageButtonRect").objectReferenceValue = languageRect;
            managerSo.FindProperty("resetPositionButtonRect").objectReferenceValue = resetRect;

            // 既存の PlayerSlot 一覧をシーンから収集 (CyanPlayerObjectPool 等で配置されたもの)
            var allSlots = FindSceneObjects<QvInfoX_PlayerSlot>(true);
            var slots = new List<QvInfoX_PlayerSlot>();
            foreach (var slot in allSlots)
                if (slot.gameObject.scene == rootGo.scene)
                    slots.Add(slot);
            var slotsProp = managerSo.FindProperty("playerSlots");
            slotsProp.arraySize = slots.Count;
            for (var i = 0; i < slots.Count; i++)
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];

            managerSo.ApplyModifiedProperties();
            manager._RefreshHoldStatus();
            SafeCopyProxyToUdon(manager);

            // Walkthrough keeps UI/Interact/pickup rays, without blocking Player/PlayerLocal.
            // Include the solid pickup handle and inactive descendants when refreshing old scenes.
            SetLayerRecursively(rootGo, WalkthroughLayer);

            EditorUtility.SetDirty(rootGo);

            Debug.Log("[QvInfoX AutoSetup] " + LanguageText(manager.language,
                $"ペン保持: 対応 {holdController.targetPens.Length}、スキップ {holdSkippedCount}。詳細は Console の [QvHold] を確認してください。",
                $"Hold pens: {holdController.targetPens.Length} compatible, {holdSkippedCount} skipped. See [QvHold] in the Console for details.",
                $"펜 유지: 호환 {holdController.targetPens.Length}, 건너뜀 {holdSkippedCount}. Console의 [QvHold]에서 상세 내용을 확인하세요."));

            return manager;
        }

        [Serializable]
        private sealed class PackageManifest
        {
            public string version;
        }

        private static string ReadPackageVersion()
        {
            try
            {
                const string assetPath = "Packages/com.yakigura.qvinfox/Editor/QvInfoX_AutoSetup.cs";
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                var packagePath = package != null ? package.resolvedPath : "Packages/com.yakigura.qvinfox";
                var manifest = JsonUtility.FromJson<PackageManifest>(File.ReadAllText(Path.Combine(packagePath, "package.json")));
                return manifest != null ? manifest.version : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static GameObject GetOrCreateChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null)
                return t.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 親の上端から sidePadding だけ左右を、topOffset だけ下へ空けた固定高さの帯を配置する。
        /// </summary>
        private static void AnchorTopBand(RectTransform rt, float sidePadding, float topOffset, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topOffset);
            rt.sizeDelta = new Vector2(-2f * sidePadding, height);
        }

        /// <summary>
        /// 親の下端から sidePadding だけ左右を、bottomOffset だけ上へ空けた固定高さの帯を配置する。
        /// </summary>
        private static void AnchorBottomBand(RectTransform rt, float sidePadding, float bottomOffset, float height)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, bottomOffset);
            rt.sizeDelta = new Vector2(-2f * sidePadding, height);
        }

        private static LayoutElement SetLayoutElement(GameObject go, float preferredHeight = -1f, float preferredWidth = -1f, float flexibleHeight = -1f, float flexibleWidth = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null)
                le = go.AddComponent<LayoutElement>();

            // このオブジェクトには Text や入れ子の LayoutGroup など、他の ILayoutElement が
            // 同居する場合がある。明示的な優先度を与えないと、どちらの値が採用されるかが
            // 不定になり (例: Header の Text が自身の折り返し計算結果で高さを上書きしてしまう)、
            // 意図したサイズにならないことがある。ここで指定したサイズを必ず優先させる。
            le.layoutPriority = 1;

            if (preferredHeight >= 0f)
                le.preferredHeight = preferredHeight;
            if (preferredWidth >= 0f)
                le.preferredWidth = preferredWidth;
            if (flexibleHeight >= 0f)
                le.flexibleHeight = flexibleHeight;
            if (flexibleWidth >= 0f)
                le.flexibleWidth = flexibleWidth;

            return le;
        }

        private static Image EnsureImage(GameObject go, Color color)
        {
            var image = go.GetComponent<Image>();
            if (image == null)
                image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static RawImage EnsureRawImage(GameObject go, Texture texture, Color color)
        {
            var oldImg = go.GetComponent<Image>();
            if (oldImg != null)
                Undo.DestroyObjectImmediate(oldImg);

            var rawImage = go.GetComponent<RawImage>();
            if (rawImage == null)
                rawImage = go.AddComponent<RawImage>();
            rawImage.texture = texture;
            rawImage.color = color;
            return rawImage;
        }

        private const string RING_SPRITE_PATH = "Assets/QvInfoX/UI/RingSprite_v2.png";
        private static Sprite _cachedRingSprite;

        /// <summary>
        /// メインビューの読込率をドーナツ (リング) 状に表示するための共通スプライトを、
        /// 手描きアセットを用意せず手続き的に生成する。円の外側と内側それぞれに
        /// 1px 分のアンチエイリアスを掛けた白いリングを 1 枚だけ作り、背景リングと
        /// 前景の Filled Radial リングの両方でこれを使い回す (色はそれぞれの Image.color で変える)。
        /// </summary>
        private static Sprite GetOrGenerateRingSprite()
        {
            if (_cachedRingSprite != null)
                return _cachedRingSprite;

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(RING_SPRITE_PATH);
            if (existing != null)
            {
                _cachedRingSprite = existing;
                return existing;
            }

            var dir = Path.GetDirectoryName(RING_SPRITE_PATH);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            const int size = 128;
            const float outerR = 0.46f;
            const float innerR = 0.40f;
            const float aa = 1.5f / size;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color[size * size];
            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x, y), center) / size;
                    var outerAlpha = 1f - Mathf.Clamp01((d - (outerR - aa)) / aa);
                    var innerAlpha = Mathf.Clamp01((d - (innerR - aa)) / aa);
                    var alpha = Mathf.Clamp01(Mathf.Min(outerAlpha, innerAlpha));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            File.WriteAllBytes(RING_SPRITE_PATH, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(RING_SPRITE_PATH);
            var importer = AssetImporter.GetAtPath(RING_SPRITE_PATH) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            _cachedRingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RING_SPRITE_PATH);
            return _cachedRingSprite;
        }

        private const string INK_PREVIEW_FOLDER = "Assets/QvInfoX/InkPreviews";

        private static Material GetPreviewInkMaterial(QvPen_PenManager pm)
        {
            return EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                ? pm.questInkMaterial
                : pm.pcInkMaterial;
        }

        /// <summary>
        /// QvPen_PenManager の colorGradient (虹色/グラデーション/Alpha) および
        /// pcInkMaterial/questInkMaterial (特殊マテリアルテクスチャ) を忠実に反映した
        /// インクプレビュー用 Texture2D を生成・アセット化する。
        /// </summary>
        private static Texture2D GetOrGenerateInkPreviewTexture(QvPen_PenManager pm, int index)
        {
            if (pm == null)
                return Texture2D.whiteTexture;

            if (!Directory.Exists(INK_PREVIEW_FOLDER))
                Directory.CreateDirectory(INK_PREVIEW_FOLDER);

            var assetPath = $"{INK_PREVIEW_FOLDER}/InkPreview_{index}.png";

            const int width = 64;
            const int height = 16;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var grad = pm.colorGradient;
            var mat = GetPreviewInkMaterial(pm);

            // 特殊マテリアルが _MainTex を持っている場合、そのテクスチャをサンプリング
            Texture2D readableMainTex = null;
            if (mat != null && mat.HasProperty("_MainTex") && mat.mainTexture is Texture2D sourceTex)
            {
                readableMainTex = GetReadableTexture(sourceTex, width, height);
            }

            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var u = (float)x / (width - 1);
                    var v = (float)y / (height - 1);

                    // 1. グラデーション色を取得 (虹色・2色・単色・Alpha すべて対応)
                    var col = grad != null ? grad.Evaluate(u) : Color.white;

                    // 2. 特殊マテリアルテクスチャがある場合は乗算合成
                    if (readableMainTex != null)
                    {
                        var texColor = readableMainTex.GetPixelBilinear(u, v);
                        col *= texColor;
                    }

                    pixels[y * width + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            if (readableMainTex != null)
                UnityEngine.Object.DestroyImmediate(readableMainTex);

            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.GUI;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Texture2D GetReadableTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            var readable = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        private static Text EnsureText(GameObject go, int fontSize, TextAnchor alignment, Color? color = null)
        {
            var text = go.GetComponent<Text>();
            if (text == null)
                text = go.AddComponent<Text>();

            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color ?? TextColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false; // Quest 最適化: 非インタラクティブ要素は Raycast OFF
            return text;
        }

        private static string LanguageText(int language, string ja, string en, string ko)
        {
            if (language == 1 && !string.IsNullOrEmpty(en)) return en;
            if (language == 2 && !string.IsNullOrEmpty(ko)) return ko;
            return ja;
        }

        private static string HealthIssueText(string issue, int language)
        {
            if (language != 1 && language != 2) return issue;
            if (issue.StartsWith("WorldState: 1 本必要です"))
                return LanguageText(language, issue, "Exactly one WorldState is required: " + issue,
                    "WorldState는 하나만 필요합니다: " + issue);
            if (issue.StartsWith("MasterTotals: 1 本必要です"))
                return LanguageText(language, issue, "Exactly one MasterTotals is required: " + issue,
                    "MasterTotals는 하나만 필요합니다: " + issue);
            if (issue == "MasterTotals が無効です") return LanguageText(language, issue, "MasterTotals is disabled", "MasterTotals가 비활성화되었습니다");
            if (issue.StartsWith("WorldState: ペン配列長 "))
                return LanguageText(language, issue,
                    issue.Replace("ペン配列長", "pen array length").Replace("がシーンのペン数", "does not match scene pen count").Replace("と一致しません", ""),
                    issue.Replace("ペン配列長", "펜 배열 길이").Replace("がシーンのペン数", "가 씬의 펜 수").Replace("と一致しません", "와 다릅니다"));
            if (issue == "WorldState が無効です") return LanguageText(language, issue, "WorldState is disabled", "WorldState가 비활성화되었습니다");
            if (issue == "EventSystem がありません") return LanguageText(language, issue, "EventSystem is missing", "EventSystem이 없습니다");
            if (issue == "PlayerPool がありません") return LanguageText(language, issue, "PlayerPool is missing", "PlayerPool이 없습니다");
            if (issue == "PlayerPool が無効です") return LanguageText(language, issue, "PlayerPool is disabled", "PlayerPool이 비활성화되었습니다");
            if (issue == "PlayerPool: CyanPlayerObjectPool がありません") return LanguageText(language, issue, "PlayerPool: CyanPlayerObjectPool is missing", "PlayerPool: CyanPlayerObjectPool이 없습니다");
            if (issue == "PlayerPool: Assigner がありません") return LanguageText(language, issue, "PlayerPool: Assigner is missing", "PlayerPool: Assigner가 없습니다");
            if (issue == "PlayerPool: PlayerSlots がありません") return LanguageText(language, issue, "PlayerPool: PlayerSlots are missing", "PlayerPool: PlayerSlots이 없습니다");
            if (issue == "PlayerPool: Assigner の poolObjectsParent が違います") return LanguageText(language, issue, "PlayerPool: Assigner poolObjectsParent is incorrect", "PlayerPool: Assigner의 poolObjectsParent가 잘못되었습니다");
            if (issue == "Monitor がありません") return LanguageText(language, issue, "Monitor is missing", "모니터가 없습니다");
            if (issue == "Monitor が無効です") return LanguageText(language, issue, "Monitor is disabled", "모니터가 비활성화되었습니다");
            if (issue == "Monitor: Manager がありません") return LanguageText(language, issue, "Monitor: Manager is missing", "모니터: Manager가 없습니다");
            if (issue == "Monitor: Rigidbody の Kinematic/Gravity 設定が違います") return LanguageText(language, issue, "Monitor: Rigidbody Kinematic/Gravity settings are incorrect", "모니터: Rigidbody Kinematic/Gravity 설정이 잘못되었습니다");
            if (issue == "Monitor: VRC_Pickup がありません、または無効です") return LanguageText(language, issue, "Monitor: VRC_Pickup is missing or disabled", "모니터: VRC_Pickup이 없거나 비활성화되었습니다");
            if (issue == "Monitor: Root の持ち手 Collider がありません、または操作面と重なります") return LanguageText(language, issue, "Monitor: handle collider is missing or overlaps the controls", "모니터: 손잡이 콜라이더가 없거나 조작부와 겹칩니다");
            if (issue == "Monitor: Canvas がありません") return LanguageText(language, issue, "Monitor: Canvas is missing", "모니터: Canvas가 없습니다");
            if (issue == "Canvas: World Space 設定がありません") return LanguageText(language, issue, "Canvas: World Space mode is missing", "Canvas: World Space 설정이 없습니다");
            if (issue == "Canvas: GraphicRaycaster がありません、または無効です") return LanguageText(language, issue, "Canvas: GraphicRaycaster is missing or disabled", "Canvas: GraphicRaycaster가 없거나 비활성화되었습니다");
            if (issue == "Canvas: VRCUiShape または Collider がありません") return LanguageText(language, issue, "Canvas: VRCUiShape or Collider is missing", "Canvas: VRCUiShape 또는 Collider가 없습니다");
            if (issue == "Manager: WorldState 参照が未配線です") return LanguageText(language, issue, "Manager: WorldState reference is missing", "Manager: WorldState 참조가 연결되지 않았습니다");
            if (issue == "Manager: MasterTotals 参照が未配線です") return LanguageText(language, issue, "Manager: MasterTotals reference is missing", "Manager: MasterTotals 참조가 연결되지 않았습니다");
            if (issue == "Manager: PlayerSlot 配列の長さがシーンと一致しません") return LanguageText(language, issue, "Manager: PlayerSlot array size does not match the scene", "Manager: PlayerSlot 배열 길이가 씬과 다릅니다");
            if (issue == "Manager: PlayerSlot 参照が不足しています") return LanguageText(language, issue, "Manager: PlayerSlot reference is missing", "Manager: PlayerSlot 참조가 없습니다");
            if (issue == "Manager: Scroll/Button/Toggle の参照が不足しています") return LanguageText(language, issue, "Manager: scroll or control references are missing", "Manager: 스크롤 또는 컨트롤 참조가 없습니다");
            if (issue == "Monitor: タブボタンが不足しています") return LanguageText(language, issue, "Monitor: tab buttons are missing", "모니터: 탭 버튼이 없습니다");
            if (issue == "Manager: Indicator 配列の長さがペン数と一致しません") return LanguageText(language, issue, "Manager: Indicator array size does not match the pen count", "Manager: Indicator 배열 길이가 펜 수와 다릅니다");
            if (issue == "Manager: Indicator 参照が不足しています") return LanguageText(language, issue, "Manager: Indicator reference is missing", "Manager: Indicator 참조가 없습니다");
            if (issue.StartsWith("PlayerPool: poolSize=")) return issue;
            if (issue.StartsWith("PlayerPool: slot ")) return LanguageText(language, issue, issue.Replace("に QvInfoX_PlayerSlot がありません", " is missing QvInfoX_PlayerSlot"), issue.Replace("に QvInfoX_PlayerSlot がありません", "에 QvInfoX_PlayerSlot이 없습니다"));
            if (issue.StartsWith("Canvas: Walkthrough 以外の Layer")) return LanguageText(language, issue, issue.Replace("Walkthrough 以外の Layer があります", "non-Walkthrough layer"), issue.Replace("Walkthrough 以外の Layer があります", "Walkthrough가 아닌 레이어"));
            if (issue.StartsWith("Indicator:")) return LanguageText(language, issue, issue.Replace("の参照または生成プレビューが不正です", " has an invalid reference or preview").Replace("個です（期待値 1）", " instances (expected 1)"), issue.Replace("の参照または生成プレビューが不正です", "의 참조 또는 미리보기가 잘못되었습니다").Replace("個です（期待値 1）", "개가 있습니다 (필요한 수: 1)"));
            if (issue.StartsWith("Manager:")) return LanguageText(language, issue, issue.Replace("は", " requires ").Replace("要素必要です", " elements").Replace("が未配線です", " is not connected"), issue.Replace("は", "에는 ").Replace("要素必要です", "개 요소가 필요합니다").Replace("が未配線です", "가 연결되지 않았습니다"));
            if (issue.Contains("Udon 永続リスナーが未配線です")) return LanguageText(language, issue,
                issue.Replace("Udon 永続リスナーが未配線です", "Udon persistent listener is missing"),
                issue.Replace("Udon 永続リスナーが未配線です", "Udon 영구 리스너가 연결되지 않았습니다"));
            if (issue.Contains("Udon イベント名が")) return LanguageText(language, issue,
                issue.Replace("Udon イベント名が", "Udon event name is not ").Replace("ではありません", ""),
                issue.Replace("Udon イベント名が", "Udon 이벤트 이름이 ").Replace("ではありません", "이 아닙니다"));
            return issue; // Japanese fallback for a new, untranslated diagnostic.
        }

        private static Button EnsureButton(GameObject go, string label)
        {
            var image = EnsureImage(go, ControlBackgroundColor);
            image.raycastTarget = true;

            var button = go.GetComponent<Button>();
            if (button == null)
                button = go.AddComponent<Button>();

            var labelGo = GetOrCreateChild(go.transform, "Label");
            StretchFill(labelGo.GetComponent<RectTransform>());
            var labelText = EnsureText(labelGo, 14, TextAnchor.MiddleCenter);
            labelText.text = label;

            return button;
        }

        private static Toggle EnsureToggle(GameObject go, string label, bool initialValue = true)
        {
            var bgImage = EnsureImage(go, ControlBackgroundColor);
            bgImage.raycastTarget = true;

            var toggle = go.GetComponent<Toggle>();
            if (toggle == null)
                toggle = go.AddComponent<Toggle>();

            var checkGo = GetOrCreateChild(go.transform, "Checkmark");
            var checkRect = checkGo.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0f, 0.5f);
            checkRect.anchorMax = new Vector2(0f, 0.5f);
            checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.anchoredPosition = new Vector2(9f, 0f);
            checkRect.sizeDelta = new Vector2(15f, 15f);
            var checkImage = EnsureImage(checkGo, AccentColor);
            checkImage.raycastTarget = false;

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.SetIsOnWithoutNotify(initialValue);

            var labelGo = GetOrCreateChild(go.transform, "Label");
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(32f, 0f);
            labelRect.offsetMax = Vector2.zero;
            var labelText = EnsureText(labelGo, 13, TextAnchor.MiddleLeft);
            labelText.text = label;

            return toggle;
        }

        /// <summary>
        /// ScrollRect + Viewport (RectMask2D) + Content の一式を構築する。
        /// </summary>
        private static ScrollRect BuildScrollRect(GameObject viewGo, out GameObject contentGo)
        {
            var scrollRect = viewGo.GetComponent<ScrollRect>();
            if (scrollRect == null)
                scrollRect = viewGo.AddComponent<ScrollRect>();

            var viewportGo = GetOrCreateChild(viewGo.transform, "Viewport");
            StretchFill(viewportGo.GetComponent<RectTransform>());
            var viewportImage = EnsureImage(viewportGo, new Color(0f, 0f, 0f, 0f));
            viewportImage.raycastTarget = true;
            var mask = viewportGo.GetComponent<RectMask2D>();
            if (mask == null)
                mask = viewportGo.AddComponent<RectMask2D>();

            contentGo = GetOrCreateChild(viewportGo.transform, "Content");
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.viewport = viewportGo.GetComponent<RectTransform>();
            scrollRect.content = contentRect;

            return scrollRect;
        }

        /// <summary>
        /// Button.onClick を UdonBehaviour.SendCustomEvent へ永続リスナーとして登録する。
        /// UdonSharp の C# デリゲート (AddVoidPersistentListener) では VRChat 実行時に
        /// Udon VM へイベントが届かず、VR でボタン操作が全くできなくなるため、
        /// 確実に UdonBehaviour.SendCustomEvent を対象として登録する。
        /// </summary>
        private static void WireButtonToUdon(Button button, UdonSharpBehaviour behaviour, string methodName)
        {
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            if (udon == null)
                udon = behaviour.GetComponent<VRC.Udon.UdonBehaviour>();

            while (button.onClick.GetPersistentEventCount() > 0)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, 0);

            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                button.onClick,
                new UnityEngine.Events.UnityAction<string>(udon.SendCustomEvent),
                methodName);
        }

        /// <summary>
        /// Toggle.onValueChanged を UdonBehaviour.SendCustomEvent へ永続リスナーとして登録する。
        /// </summary>
        private static void WireToggleToUdon(Toggle toggle, UdonSharpBehaviour behaviour, string methodName)
        {
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            if (udon == null)
                udon = behaviour.GetComponent<VRC.Udon.UdonBehaviour>();

            while (toggle.onValueChanged.GetPersistentEventCount() > 0)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(toggle.onValueChanged, 0);

            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                toggle.onValueChanged,
                new UnityEngine.Events.UnityAction<string>(udon.SendCustomEvent),
                methodName);
        }

        /// <summary>
        /// Canvas 下端の外側に丸みのある持ち手を置く。把持判定は Root の BoxCollider が担う。
        /// </summary>
        private static void SetupHandleBar(Transform rootTransform)
        {
            var barTrans = rootTransform.Find("HandleBar");
            if (barTrans == null)
            {
                var barGo = new GameObject("HandleBar");
                Undo.RegisterCreatedObjectUndo(barGo, "Create QvInfoX handle");
                barGo.transform.SetParent(rootTransform, false);
                barTrans = barGo.transform;
            }

            Undo.RecordObject(barTrans, "Position QvInfoX handle");
            barTrans.localPosition = new Vector3(0f, -0.33f, 0f);
            barTrans.localRotation = Quaternion.identity;
            barTrans.localScale = Vector3.one;

            foreach (var collider in barTrans.GetComponents<Collider>())
                Undo.DestroyObjectImmediate(collider);

            // Replace the old primitive Cube; keep the generated hierarchy on subsequent runs.
            var model = barTrans.Find("Model");
            if (model != null && model.GetComponent<MeshFilter>() != null)
            {
                Undo.DestroyObjectImmediate(model.gameObject);
                model = null;
            }
            if (model == null)
            {
                var modelGo = new GameObject("Model");
                Undo.RegisterCreatedObjectUndo(modelGo, "Create QvInfoX handle model");
                modelGo.transform.SetParent(barTrans, false);
                model = modelGo.transform;
            }
            Undo.RecordObject(model, "Configure QvInfoX handle model");
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            foreach (var collider in model.GetComponents<Collider>())
                Undo.DestroyObjectImmediate(collider);

            var shader = Shader.Find("Unlit/Color");
            var teal = new Material(shader) { color = new Color(43f / 255f, 179f / 255f, 163f / 255f, 1f) };
            var dots = new Material(shader) { color = TextColor };

            // A cylinder and two spheres retain circular ends without stretching the caps.
            CreateHandlePart(model, "Body", PrimitiveType.Cylinder, Vector3.zero,
                Quaternion.Euler(0f, 0f, 90f), new Vector3(0.025f, 0.0775f, 0.025f), teal);
            CreateHandlePart(model, "LeftCap", PrimitiveType.Sphere, new Vector3(-0.0775f, 0f, 0f),
                Quaternion.identity, Vector3.one * 0.025f, teal);
            CreateHandlePart(model, "RightCap", PrimitiveType.Sphere, new Vector3(0.0775f, 0f, 0f),
                Quaternion.identity, Vector3.one * 0.025f, teal);
            for (int i = 0; i < 3; i++)
                CreateHandlePart(model, "Dot" + i, PrimitiveType.Sphere,
                    new Vector3((i - 1) * 0.014f, 0f, -0.0125f), Quaternion.identity,
                    Vector3.one * 0.004f, dots);
        }

        private static void CreateHandlePart(Transform parent, string name, PrimitiveType type,
            Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var part = parent.Find(name);
            if (part == null)
            {
                var partGo = GameObject.CreatePrimitive(type);
                partGo.name = name;
                Undo.RegisterCreatedObjectUndo(partGo, "Create QvInfoX handle part");
                partGo.transform.SetParent(parent, false);
                part = partGo.transform;
            }
            Undo.RecordObject(part, "Configure QvInfoX handle part");
            part.localPosition = position;
            part.localRotation = rotation;
            part.localScale = scale;
            foreach (var collider in part.GetComponents<Collider>())
                Undo.DestroyObjectImmediate(collider);
            var renderer = part.GetComponent<Renderer>();
            Undo.RecordObject(renderer, "Color QvInfoX handle part");
            renderer.sharedMaterial = material;
        }
        private static void CheckPanelColliders(Transform root, List<string> issues)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                if (!collider.isTrigger && (!Physics.GetIgnoreLayerCollision(collider.gameObject.layer, 9)
                    || !Physics.GetIgnoreLayerCollision(collider.gameObject.layer, 10)))
                    issues.Add("UI: player-blocking Collider: " + collider.name + " (Walkthrough に変更してください)");
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            var t = go.transform;
            for (var i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
        }

        /// <summary>
        /// Quest / Android 最適化: ボタン・トグル等のインタラクティブ要素以外の
        /// Raycast Target をすべて OFF にする。GraphicRaycaster 自体は常時 ON のまま維持する。
        /// </summary>
        private static void DisableNonInteractiveRaycastTargets(GameObject canvasRoot)
        {
            var graphics = canvasRoot.GetComponentsInChildren<Graphic>(true);
            foreach (var g in graphics)
            {
                if (g == null)
                    continue;

                // ボタン/トグル本体、ScrollRect のドラッグ判定を受け取る Viewport、
                // および EventTrigger でポインタ判定を受けるハンドルバーの Graphic は
                // Raycast Target を維持する。それ以外 (装飾用の背景・テキスト等) は OFF。
                var isInteractive = g.GetComponent<Selectable>() != null
                    || g.GetComponent<UnityEngine.EventSystems.EventTrigger>() != null
                    || g.gameObject.name == "Viewport";
                g.raycastTarget = isInteractive;
            }
        }

        #endregion
    }
}
