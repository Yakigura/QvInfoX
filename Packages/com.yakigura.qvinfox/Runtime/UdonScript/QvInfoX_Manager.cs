// ----------------------------------------------------------------------
// QvInfoX - Non-invasive QvPen monitoring tools
// ----------------------------------------------------------------------

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using QvPen.UdonScript;
using Yakigura.QvInkPreview;
using Stopwatch = System.Diagnostics.Stopwatch;

#pragma warning disable IDE0090, IDE1006

namespace QvInfoX
{
    /// <summary>
    /// QvInfoX 監視パネルの中枢ロジック。
    /// QvPen 本体を一切改変せず、以下の公開 API のみを読み取り専用で参照して集計する。
    ///  - QvPen_Pen.inkPoolRootName (public const) / QvPen_LateSync.pen / InkPoolSynced / InkPoolNotSynced (public)
    ///  - QvPen_PenManager.colorGradient / inkWidth (public)
    ///  - QvInfoX_PlayerSlot (本パッケージが管理する同期スロット)
    /// </summary>
    [AddComponentMenu("QvInfoX/QvInfoX Manager")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvInfoX_Manager : UdonSharpBehaviour
    {
        #region Settings

        [Tooltip("公開シーンでは false。入室時の共通ログ権限制御で作者だけ true にする。")]
        public bool debugLogEnabled = true;
        public UdonSharpBehaviour diag537Authority;

        // Diagnostic-only protocol. Serialized references survive PlayerObject cloning.
        public bool diag537SharedRequired;

        private void WriteDiagnosticLine(string message, int level)
        {
#if QVINFOX_DIAG
            if (Utilities.IsValid(diag537Authority))
            {
                diag537Authority.SetProgramVariable("diag537Message", message);
                diag537Authority.SetProgramVariable("diag537Level", level);
                diag537Authority.SendCustomEvent("_Write537Line");
                return;
            }
            if (diag537SharedRequired) return;
            if (level == 2)
            {
                if (debugLogEnabled) Debug.LogError(message);
            }
            else if (level == 1)
            {
                if (debugLogEnabled) Debug.LogWarning(message);
            }
            else
            {
                if (debugLogEnabled) Debug.Log(message);
            }
#endif
        }


        // Wired issue-537 scenes share the same budget for old and new tags.
        // Unwired standalone packages retain their existing diagnostic switch.
        private bool TakeDiagnosticLine()
        {
            if (!debugLogEnabled) return false;
            if (!Utilities.IsValid(diag537Authority)) return !diag537SharedRequired;
            diag537Authority.SendCustomEvent("_Take537Line");
            return (bool)diag537Authority.GetProgramVariable("_out537Permit");
        }

        private bool Diag537Take()
        {
            if (!debugLogEnabled || !Utilities.IsValid(diag537Authority)) return false;
            diag537Authority.SendCustomEvent("_Take537Line");
            return (bool)diag537Authority.GetProgramVariable("_out537Permit");
        }

        private bool diag537MasterPending;
        private int diag537PreviousMaster, diag537Master, diag537LocalId;
        private float diag537MasterAt;

        // Retry rejected diagnostic admission at most once per second.
        private float diag537NextRetryAt;

        private void Diag537Flush()
        {
            if (Time.time < diag537NextRetryAt) return;
            if (!debugLogEnabled || !diag537MasterPending) return;
            if (!Diag537Take()) { diag537NextRetryAt = Time.time + 1f; return; }
            if (debugLogEnabled) WriteDiagnosticLine("[Qv537Diag] C master t=" + diag537MasterAt
                + " event=OnMasterClientSwitched previous=" + diag537PreviousMaster
                + " master=" + diag537Master + " localId=" + diag537LocalId, 0);
            diag537MasterPending = false;
        }


        [Header("更新設定")]
        [Tooltip("自動更新の周期 (秒)")]
        public float autoUpdateInterval = 10f;

        [Tooltip("手動更新ボタン押下後のクールダウン (秒)")]
        public float manualUpdateCooldown = 10f;

        [Tooltip("0: 日本語, 1: English, 2: 한국어。範囲外は日本語")]
        public int language = 0;

        #endregion

        #region References

        [Header("プレイヤースロット (CyanPlayerObjectPool 等で配置)")]
        public QvInfoX_PlayerSlot[] playerSlots = new QvInfoX_PlayerSlot[0];

        [Header("ワールド固定の Master 集計")]
        public QvInfoX_WorldState worldState;
        public QvInfoX_MasterTotals masterTotals;

        [Tooltip("任意の外部同期進捗。公開変数 externalSync* を読み取り、未設定なら従来表示を使う。")]
        public UdonSharpBehaviour externalProgressSource;

        [Header("ペン Indicator 一覧 (一括トグル用)")]
        public QvInfoX_Indicator[] indicators = new QvInfoX_Indicator[0];
        public QvInkPreview_Manager inkPreviewManager;

        [Header("メインビュー UI")]
        public Transform rowsContainer;
        public Text[] rowPool = new Text[0];
        public Text[] rowMetaTexts = new Text[0];
        public Text[] rowAuthoredTexts = new Text[0];
        // The ring and the percent are separate so both remain legible in VR.
        public Image[] rowProgressBars = new Image[0];
        public Text[] rowPercentTexts = new Text[0];
        public ScrollRect mainScrollRect;

        [Header("負荷の高いペン Top3")]
        public Text top3Text;

        [Header("フッター")]
        public Button manualUpdateButton;
        public Text footerStatusText;
        public Toggle indicatorToggle;
        public Button languageButton;
        public Text languageButtonText;
        public Button resetPositionButton;
        public Text resetPositionButtonText;
        public Text manualButtonText;
        public Text indicatorToggleText;
        public QvInfoX_HoldController holdController;
        public Toggle holdToggle;
        public Text holdToggleText;
        public Text holdStatusText;
        public Text mainTabText;
        public Text detailTabText;

        [Header("タブ / 詳細ビュー")]
        public GameObject mainViewRoot;
        public GameObject detailViewRoot;
        public ScrollRect detailScrollRect;
        // §5.4 / §7.4: ink preview, horizontal progress rail, percent, and pen details.
        public RawImage[] detailInkLines = new RawImage[0];
        public Image[] detailProgressBars = new Image[0];
        public Text[] detailPercentTexts = new Text[0];
        public Text[] detailRowTexts = new Text[0];
        public Text[] detailMetaTexts = new Text[0];
        public Image mainTabImage;
        public Image detailTabImage;

        [Header("デスクトップ用の直接ヒットテスト対象 (QvPalette の _PollDesktopInteraction と同じ方式)")]
        [Tooltip("VRChat の World Space UI はデスクトップだと画面中央のクロスヘアでしか狙えず、" +
            "小さいボタンを正確にクリックするのは非現実的 (QvKeep 同様の既知の制約)。" +
            "そのため Unity 標準の UI クリック経路には頼らず、頭の向きに飛ばしたレイと" +
            "各要素の RectTransform を直接突き合わせて判定する。")]
        [SerializeField] private RectTransform manualUpdateButtonRect;
        [SerializeField] private RectTransform mainTabRect;
        [SerializeField] private RectTransform detailTabRect;
        [SerializeField] private RectTransform indicatorToggleRect;
        [SerializeField] private RectTransform holdToggleRect;
        [SerializeField] private RectTransform languageButtonRect;
        [SerializeField] private RectTransform resetPositionButtonRect;

        [Header("Monitor initial pose")]
        public bool initialPoseCaptured;
        public Vector3 initialLocalPosition;
        public Quaternion initialLocalRotation = Quaternion.identity;

        #endregion

        #region State

        private bool hasReceivedFirstSync = false;
        private float lastManualUpdateTime = -999f;
        private float lastRosterUpdateTime = -999f;
        private int lastObservedMasterId = -1;
        private bool lastRenderedMasterDataValid = false;
        private int lastRenderedMasterReportRevision = 0;
        private int masterReportFreshAfter = 0;
        private bool masterReportRetryPending = false;
        private bool masterReportRequestPending = false;
        private const int MASTER_TOTAL_MAX_AGE_MS = 90000;

        // §3.2: 全員の読込がおおよそ何秒で終わるかの目安 (RenderPlayerRows が算出し、ヘッダーの
        // RenderTop3 で表示する)。現在ダウンロード中のプレイヤーの中で最も遅い人の ETA を採用する
        // (その人が終われば全員終わっている、という一番わかりやすい基準のため)。
        // -1 は「対象者なし (誰も同期中でない、またはマスターデータ未確定)」を表す。
        private float overallEtaSeconds = -1f;

        // 外部同期状態は構成時のみ最大 1 秒間隔で軽量に再描画する。0: 従来表示、1: 未完了、2: 停止の可能性。
        private int externalProgressStatus;
        private int externalProgressReceived;
        private int externalProgressTotal;
        private float nextExternalProgressAt;
        private bool externalFastSyncEnabled;
        private int externalFastSyncWire = int.MinValue | (0xFFFFF << 11);
        private int externalLatestGeneration = -1;
        private int externalReportedState;
        private bool overallEtaPending;
        // Resolved once, before sorting. All arrays are keyed by slot, not row order.
        private int[] resolvedStatuses = new int[0];
        private int[] resolvedFastStates = new int[0];
        private bool[] resolvedKnown = new bool[0];
        private bool[] resolvedComplete = new bool[0];

        // 速度/ETA 計算用の直近サンプル (playerSlots と同じインデックスで対応)
        private int[] prevSyncedStrokes = new int[0];
        private float[] prevSampleTime = new float[0];
        private float[] estimatedSpeed = new float[0];
        private bool[] hasSpeedSample = new bool[0];
        private int[] sampledPlayerIds = new int[0];
        private int[] zeroSpeedStreak = new int[0];
        private int sampledMasterId = -1;
        private int[] warningPreviousStrokes = new int[0];
        private int[] warningSampledPlayerIds = new int[0];
        private bool[] hasWarningBaseline = new bool[0];
        private bool warningObservationPending;

        // Join diagnostics are deliberately kept apart from the display/measurement state.
        // They exist only while the author enables debugLogEnabled and retain a compact
        // last-seen snapshot so repeated network callbacks do not create log noise.
        private bool[] joinDiagnosticSlotSeen = new bool[0];
        private int[] joinDiagnosticAssignedIds = new int[0];
        private int[] joinDiagnosticOwnerIds = new int[0];
        private int[] joinDiagnosticSyncedStrokes = new int[0];
        private string[] joinDiagnosticPlayerNames = new string[0];
        private string[] joinDiagnosticOwnerNames = new string[0];

        // Display diagnostics only: no synchronized fields or progress decisions.
        // Eight ints per slot: player ID, received, denominator, status, flags,
        // world strokes, external received, external total. Positive player IDs
        // distinguish the first observation from the zero-initialized cache.
        private const int ROW_DIAGNOSTIC_WIDTH = 8;
        private const int ROW_DIAGNOSTIC_MAX_LINES = 256;
        private int[] rowDiagnosticSnapshot = new int[0];
        private float[] rowDiagnosticPercents = new float[0];
        private int rowDiagnosticLineCount;
        private float rowDiagnosticNextRetryAt;

        // Auto Setup persists this scene order for all clients.
        [HideInInspector] public QvPen_Pen[] penOrder;

        private const int MAX_PENS = 32;
        private readonly Transform[] penSyncerRoots = new Transform[MAX_PENS];
        private int penCount = 0;

        // RefreshAll 内での重複走査を削減するためのペン別ストローク数・頂点数キャッシュ
        private readonly int[] cachedPenStrokes = new int[MAX_PENS];
        private readonly int[] cachedPenVertices = new int[MAX_PENS];
        private int cachedTotalStrokes = 0;
        private int cachedTotalVertices = 0;
        private int cachedSyncedStrokes = 0;
        private int cachedSyncedVertices = 0;
        private readonly int[] cachedPenSyncedStrokes = new int[MAX_PENS];

        // §3.3: プレイヤー別の「実際に描いたストローク数」の集計結果 (playerId → 本数)。
        // BuildAuthorStrokeTally が RefreshAll のたびに再構築する。
        private int[] authorPlayerIds = new int[0];
        private int[] authorStrokeCounts = new int[0];
        private int authorTallyCount = 0;

        // §3.3: インスタンスを抜けたプレイヤーもグレーアウト表示で一覧に残すための記録。
        // CyanPlayerObjectPool のスロットは退出後すぐ次の参加者に再利用されるため、
        // ここに playerId/名前を別途保持しておく (最大 MAX_DEPARTED 人分)。
        private const int MAX_DEPARTED = 32;
        private readonly int[] departedPlayerIds = new int[MAX_DEPARTED];
        private readonly string[] departedPlayerNames = new string[MAX_DEPARTED];
        private int departedCount = 0;

        // RenderPlayerRows での GC Allocation をゼロにするための再利用ソートバッファ
        private int[] orderBuffer = new int[0];
        private float[] sortScoresBuffer = new float[0];
        private float[] percentsBuffer = new float[0];

        // UdonSharp はユーザー定義型 (UdonSharpBehaviour) 上の static フィールドをサポートしないため、
        // インスタンスフィールドとして持つ (この Manager は通常シーンに1つしか置かれない想定)。
        private readonly Color NormalRowColor = new Color(0.95f, 0.97f, 0.98f, 1f);
        private readonly Color MutedRowColor = new Color(0.68f, 0.78f, 0.83f, 1f);
        private readonly Color DepartedRowColor = new Color(0.55f, 0.63f, 0.67f, 1f);
        private readonly Color WarningBarColor = new Color(0.96f, 0.47f, 0.47f, 1f);
        private readonly Color SyncedBarColor = new Color(0.29f, 0.83f, 0.78f, 1f);
        private readonly Color DownloadingBarColor = new Color(0.40f, 0.68f, 0.97f, 1f);
        private readonly Color PendingBarColor = new Color(0.96f, 0.74f, 0.38f, 1f);
        private readonly Color ActiveTabColor = new Color(0.08f, 0.38f, 0.38f, 1f);
        private readonly Color InactiveTabColor = new Color(0.15f, 0.22f, 0.28f, 1f);

        #endregion

        private void Start()
        {
            BindSlotDiagnostics();
            // Request a fresh snapshot after joining; the owner ID also rejects
            // a departed Master's snapshot before this request is served.
            masterReportFreshAfter = Networking.GetServerTimeInMilliseconds();
            // タブの既定状態は常にメインビュー (編集時の保存状態に関わらず起動直後は必ずここに揃える)。
            _ShowMainView();
            // Controller initialization is idempotent, irrespective of Start order.
            if (Utilities.IsValid(holdController)) holdController._Initialize();
            ApplyLanguageLabels();

            DiscoverPens();
            if (Utilities.IsValid(indicatorToggle))
                _SetAllIndicatorsEnabled(indicatorToggle.isOn);
            SendCustomEventDelayedSeconds(nameof(_AutoUpdateTick), autoUpdateInterval);
            // Request after this client's freshness threshold and a later server tick.
            ScheduleMasterReportRequest();

            RefreshAll();
        }

        private string L(string ja, string en, string ko)
        {
            if (language == 1 && !string.IsNullOrEmpty(en)) return en;
            if (language == 2 && !string.IsNullOrEmpty(ko)) return ko;
            return ja;
        }

        private static string FitPlayerName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var units = 0;
            for (var i = 0; i < name.Length; i++)
            {
                units += name[i] > 0x7f ? 2 : 1;
                if (units > 27) return name.Substring(0, i) + "…";
            }
            return name;
        }

        private void ApplyLanguageLabels()
        {
            if (Utilities.IsValid(mainTabText))
                mainTabText.text = L("プレイヤー", "Players", "플레이어");
            if (Utilities.IsValid(detailTabText))
                detailTabText.text = L("ペン詳細", "Pens", "펜 상세");
            if (Utilities.IsValid(manualButtonText))
                manualButtonText.text = L("今すぐ更新", "Refresh", "새로 고침");
            if (Utilities.IsValid(indicatorToggleText))
                indicatorToggleText.text = L("ペン表示", "Show pens", "펜 표시");
            if (Utilities.IsValid(holdToggleText))
                holdToggleText.text = L("ペン保持", "Hold pens", "펜 유지");
            _RefreshHoldStatus();
            if (Utilities.IsValid(languageButtonText))
                languageButtonText.text = language == 1 ? "EN" : language == 2 ? "한국어" : "日本語";
            if (Utilities.IsValid(resetPositionButtonText))
                resetPositionButtonText.text = L("位置戻す", "Reset", "위치 복원");
        }

        public void _ResetPosition()
        {
            if (!initialPoseCaptured) return;
            transform.localPosition = initialLocalPosition;
            transform.localRotation = initialLocalRotation;
        }

        public void _CycleLanguage()
        {
            // デスクトップでは直接ヒットテストが先に同じ操作を済ませている。同じクリックで届く
            // 標準 UI 側のイベントは 1 回だけ捨てる (言語が 2 つ進むのを防ぐ)。
            if (desktopLanguageGuarded && IsDesktopGuardActive())
            {
                desktopLanguageGuarded = false;
                return;
            }

            CycleLanguageNow();
        }

        private void CycleLanguageNow()
        {
            language = language < 0 || language >= 2 ? 0 : language + 1;
            ApplyLanguageLabels();
            RenderPlayerRows();
            RenderTop3();
            RenderDetailView();
        }

        #region Pen discovery (non-invasive scene introspection)

        /// <summary>
        /// QvPen_Pen.inkPoolRootName ("QvPen_Objects") 配下を走査し、
        /// 各ペンの QvPen_LateSync (InkPoolSynced / InkPoolNotSynced を公開) を収集する。
        /// </summary>
        private void DiscoverPens()
        {
            penCount = 0;

            var pens = new Transform[MAX_PENS];
            var orderCount = penOrder == null ? 0 : penOrder.Length;
            var order = new Transform[orderCount];
            for (var i = 0; i < orderCount; i++)
                order[i] = Utilities.IsValid(penOrder[i]) ? penOrder[i].transform : null;
            var rootGo = GameObject.Find("/" + QvPen_Pen.inkPoolRootName);
            if (!Utilities.IsValid(rootGo))
            {
                return;
            }

            var root = rootGo.transform;
            var childCount = root.childCount;

            for (var i = 0; i < childCount && penCount < MAX_PENS; i++)
            {
                var child = root.GetChild(i);
                if (!Utilities.IsValid(child))
                    continue;

                var syncer = child.GetComponent<QvPen_LateSync>();
                if (!Utilities.IsValid(syncer))
                    continue;

                penSyncerRoots[penCount] = child;
                pens[penCount] = Utilities.IsValid(syncer.pen) ? syncer.pen.transform : null;
                penCount++;
            }

            SortPenRoots(penSyncerRoots, pens, penCount, order);
        }

        // Stable insertion sort keeps equal and unlisted pens in discovery order.
        private static void SortPenRoots(Transform[] roots, Transform[] pens, int count, Transform[] order)
        {
            var useOrder = order != null && order.Length > 0;
            for (var i = 1; i < count; i++)
            {
                var root = roots[i];
                var pen = pens[i];
                var key = useOrder ? FindPenOrderIndex(pen, order) : -1;
                if (key < 0 && useOrder) key = order.Length;
                var j = i - 1;
                while (j >= 0)
                {
                    var previousKey = useOrder ? FindPenOrderIndex(pens[j], order) : -1;
                    if (previousKey < 0 && useOrder) previousKey = order.Length;
                    var comparison = useOrder ? previousKey - key : ComparePenHierarchy(pens[j], pen);
                    if (comparison <= 0) break;
                    roots[j + 1] = roots[j];
                    pens[j + 1] = pens[j];
                    j--;
                }
                roots[j + 1] = root;
                pens[j + 1] = pen;
            }
        }

        private static int FindPenOrderIndex(Transform pen, Transform[] order)
        {
            if (pen == null || order == null) return -1;
            for (var i = 0; i < order.Length; i++)
                if (order[i] == pen) return i;
            return -1;
        }

        // Compare root-to-leaf sibling-index paths; an ancestor precedes its descendants.
        private static int ComparePenHierarchy(Transform first, Transform second)
        {
            if (first == second) return 0;
            if (first == null) return 1;
            if (second == null) return -1;

            var firstDepth = 0;
            var secondDepth = 0;
            for (var node = first; node != null; node = node.parent) firstDepth++;
            for (var node = second; node != null; node = node.parent) secondDepth++;

            var commonDepth = firstDepth < secondDepth ? firstDepth : secondDepth;
            for (var level = 0; level < commonDepth; level++)
            {
                var firstNode = first;
                var secondNode = second;
                for (var step = level + 1; step < firstDepth; step++) firstNode = firstNode.parent;
                for (var step = level + 1; step < secondDepth; step++) secondNode = secondNode.parent;
                var firstIndex = firstNode.GetSiblingIndex();
                var secondIndex = secondNode.GetSiblingIndex();
                if (firstIndex < secondIndex) return -1;
                if (firstIndex > secondIndex) return 1;
            }

            if (firstDepth < secondDepth) return -1;
            if (firstDepth > secondDepth) return 1;
            return 0;
        }

        private int CountStrokesInPool(Transform pool)
        {
            return Utilities.IsValid(pool) ? pool.childCount : 0;
        }

        private int CountVerticesInPool(Transform pool)
        {
            if (!Utilities.IsValid(pool))
                return 0;

            var total = 0;
            var colliders = pool.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0, n = colliders.Length; i < n; i++)
            {
                var collider = colliders[i];
                if (Utilities.IsValid(collider) && Utilities.IsValid(collider.sharedMesh))
                    total += collider.sharedMesh.vertexCount;
            }

            return total;
        }

        /// <summary>
        /// 全ペンの同期・非同期プールを1回走査し、ペンごとのストローク数・頂点数および合計をキャッシュする。
        /// </summary>
        private void ScanLocalPens()
        {
            cachedTotalStrokes = 0;
            cachedTotalVertices = 0;
            cachedSyncedStrokes = 0;
            cachedSyncedVertices = 0;

            for (var i = 0; i < penCount; i++)
            {
                var syncer = penSyncerRoots[i].GetComponent<QvPen_LateSync>();
                if (!Utilities.IsValid(syncer))
                {
                    cachedPenStrokes[i] = 0;
                    cachedPenVertices[i] = 0;
                    cachedPenSyncedStrokes[i] = 0;
                    continue;
                }

                var syncedStrokes = CountStrokesInPool(syncer.InkPoolSynced);
                var syncedVertices = CountVerticesInPool(syncer.InkPoolSynced);
                var strokes = syncedStrokes + CountStrokesInPool(syncer.InkPoolNotSynced);
                var vertices = syncedVertices + CountVerticesInPool(syncer.InkPoolNotSynced);

                cachedPenStrokes[i] = strokes;
                cachedPenVertices[i] = vertices;
                cachedPenSyncedStrokes[i] = syncedStrokes;
                cachedTotalStrokes += strokes;
                cachedTotalVertices += vertices;
                cachedSyncedStrokes += syncedStrokes;
                cachedSyncedVertices += syncedVertices;
            }
        }

        #endregion

        #region Author stroke tally (誰が何本描いたか)

        /// <summary>
        /// QvPen_Pen は各ストロークに描画者の playerId を非侵襲に読み取れる形で埋め込んでいる
        /// (QvPenUtilities.TryGetIdFromInk / EulerAnglesToPlayerId、いずれも公開 API)。
        /// これを使い、現在ローカルに存在する全ストロークをプレイヤー別に集計する。
        /// ローカルにまだ同期し切れていないストロークは数えられないため、この本数は
        /// 「このクライアントが認識できている範囲」の値であり、同期が完了するまでは
        /// 実際の本数より少なく表示されうる (他の集計値と同じ前提)。
        /// </summary>
        private void BuildAuthorStrokeTally()
        {
            var capacity = playerSlots.Length > 0 ? playerSlots.Length : 82;
            if (authorPlayerIds.Length != capacity)
            {
                authorPlayerIds = new int[capacity];
                authorStrokeCounts = new int[capacity];
            }
            authorTallyCount = 0;

            var totalStrokesSeen = cachedTotalStrokes;

            for (var i = 0; i < penCount; i++)
            {
                var syncer = penSyncerRoots[i].GetComponent<QvPen_LateSync>();
                if (!Utilities.IsValid(syncer))
                    continue;

                TallyPoolAuthors(syncer.InkPoolSynced);
                TallyPoolAuthors(syncer.InkPoolNotSynced);
            }

            var totalAttributed = 0;
            for (var i = 0; i < authorTallyCount; i++)
                totalAttributed += authorStrokeCounts[i];

            if (totalAttributed != totalStrokesSeen)
            {
                // 上の TallyPoolAuthors 内の警告で個別のストロークまでは特定できないケース
                // (所有者IDの取得自体は成功しているが、現在シーンにいないプレイヤーの ID に
                // 復号されている等) をまとめて把握できるよう、差分を残しておく。
                if (debugLogEnabled && TakeDiagnosticLine()) WriteDiagnosticLine($"[QvInfoX] 作者別集計の合計 ({totalAttributed}) が総ストローク数 ({totalStrokesSeen}) と一致しません。差分 {totalStrokesSeen - totalAttributed} 本。", 1);
            }
        }

        private void TallyPoolAuthors(Transform pool)
        {
            if (!Utilities.IsValid(pool))
                return;

            for (int i = 0, n = pool.childCount; i < n; i++)
            {
                var stroke = pool.GetChild(i);
                if (!Utilities.IsValid(stroke))
                    continue;

                if (!QvPenUtilities.TryGetIdFromInk(stroke.gameObject, out var _penIdVector, out var _inkIdVector, out var ownerIdVector))
                {
                    // この本数だけ「描いたストローク数」の合計から漏れる (総ストローク数の集計は
                    // childCount ベースで別に取っているため、そちらとは一致しなくなる)。
                    // どのストロークで発生しているか追えるよう警告を残す。
                    if (debugLogEnabled && TakeDiagnosticLine()) WriteDiagnosticLine($"[QvInfoX] ストロークの所有者IDを取得できませんでした (作者別集計から除外): {stroke.name}", 1);
                    continue;
                }

                AddAuthorStroke(QvPenUtilities.EulerAnglesToPlayerId(ownerIdVector));
            }
        }

        private void AddAuthorStroke(int playerId)
        {
            for (var i = 0; i < authorTallyCount; i++)
            {
                if (authorPlayerIds[i] == playerId)
                {
                    authorStrokeCounts[i]++;
                    return;
                }
            }

            if (authorTallyCount < authorPlayerIds.Length)
            {
                authorPlayerIds[authorTallyCount] = playerId;
                authorStrokeCounts[authorTallyCount] = 1;
                authorTallyCount++;
            }
        }

        private int GetAuthoredStrokeCount(int playerId)
        {
            for (var i = 0; i < authorTallyCount; i++)
            {
                if (authorPlayerIds[i] == playerId)
                    return authorStrokeCounts[i];
            }

            return 0;
        }

        #endregion

        #region Local master reporting

        /// <summary>
        /// 自分がマスターの場合、現在の全ペンの実測値を WorldState に書き込む。
        /// </summary>
        private void ReportIfMaster()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer) || !Networking.LocalPlayer.isMaster)
                return;

            if (!Utilities.IsValid(worldState))
                return;

            var perPen = new int[penCount];
            for (var i = 0; i < penCount; i++)
                perPen[i] = cachedPenSyncedStrokes[i];

            worldState._ReportMasterTotals(cachedTotalStrokes, cachedTotalVertices,
                cachedSyncedStrokes, cachedSyncedVertices, perPen);
            if (Utilities.IsValid(masterTotals))
                masterTotals._ReportMasterTotals(cachedTotalStrokes, cachedTotalVertices,
                    cachedSyncedStrokes, cachedSyncedVertices);
            // A transfer can invalidate and report within the same server millisecond.
            // Keep the strict freshness check, then publish once time has advanced.
            if (Utilities.IsValid(masterTotals)
                && masterTotals.masterTotalsCapturedAt - masterReportFreshAfter <= 0)
                ScheduleMasterReportRetry();
        }

        private void ScheduleMasterReportRetry()
        {
            if (masterReportRetryPending)
                return;

            masterReportRetryPending = true;
            SendCustomEventDelayedSeconds(nameof(_RetryMasterReport), 0.1f);
        }

        private void ScheduleMasterReportRequest()
        {
            if (masterReportRequestPending)
                return;

            masterReportRequestPending = true;
            SendCustomEventDelayedSeconds(nameof(_RequestMasterReport), 0.2f);
        }

        /// <summary>
        /// 自分の late-sync-ON ストローク数を自分のスロットへ報告する。
        /// </summary>
        private void ReportLocalSyncState()
        {
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer))
                return;

            var mySlot = FindSlotForPlayer(localPlayer.playerId);
            if (!Utilities.IsValid(mySlot) || !Networking.IsOwner(mySlot.gameObject))
                return;

            int value = externalFastSyncEnabled ? externalFastSyncWire : cachedSyncedStrokes;
            if (!externalFastSyncEnabled || mySlot.syncedTotalStrokes != value)
                mySlot._ReportLocalTotals(value);
        }

        private QvInfoX_PlayerSlot FindSlotForPlayer(int playerId)
        {
            for (int i = 0, n = playerSlots.Length; i < n; i++)
            {
                var slot = playerSlots[i];
                if (Utilities.IsValid(slot) && slot.assignedPlayerId == playerId)
                    return slot;
            }

            return null;
        }

        // The slot owns the network callbacks; the manager owns the author-only
        // flag and all diagnostic string construction.
        private void BindSlotDiagnostics()
        {
            for (var i = 0; i < playerSlots.Length; i++)
            {
                if (Utilities.IsValid(playerSlots[i]))
                    playerSlots[i].diagnosticsManager = this;
            }
        }

        public void _OnSlotDiagnosticChanged(QvInfoX_PlayerSlot slot, string updateCall)
        {
            if (!debugLogEnabled)
                return;

            var slotIndex = -1;
            for (var i = 0; i < playerSlots.Length; i++)
            {
                if (playerSlots[i] == slot)
                {
                    slotIndex = i;
                    break;
                }
            }

            if (slotIndex < 0)
                return;

            LogSlotDiagnosticIfChanged(slotIndex, slot, updateCall);
        }

        private void ObserveSlotDiagnostics(string updateCall)
        {
            if (!debugLogEnabled)
                return;

            for (var i = 0; i < playerSlots.Length; i++)
            {
                var slot = playerSlots[i];
                if (Utilities.IsValid(slot))
                    LogSlotDiagnosticIfChanged(i, slot, updateCall);
            }
        }

        private void LogSlotDiagnosticIfChanged(int slotIndex, QvInfoX_PlayerSlot slot, string updateCall)
        {
            if (!debugLogEnabled || Time.time < diag537NextRetryAt) return;
            EnsureSlotDiagnosticCapacity();

            var ownerId = -1;
            var ownerName = "";
            if (Utilities.IsValid(slot.Owner))
            {
                ownerId = slot.Owner.playerId;
                ownerName = slot.Owner.displayName;
            }

            var changed = !joinDiagnosticSlotSeen[slotIndex]
                || joinDiagnosticAssignedIds[slotIndex] != slot.assignedPlayerId
                || joinDiagnosticOwnerIds[slotIndex] != ownerId
                || joinDiagnosticSyncedStrokes[slotIndex] != slot.syncedTotalStrokes
                || joinDiagnosticPlayerNames[slotIndex] != slot.playerName
                || joinDiagnosticOwnerNames[slotIndex] != ownerName;
            if (!changed)
                return;

            // An untouched pool slot is the baseline, not a roster change. A slot
            // already assigned when this manager starts is still recorded below.
            var wasSeen = joinDiagnosticSlotSeen[slotIndex];

            // Snapshot admission must succeed before changing the observed cache.
            // A rejected allocation is retried after one second; accepted
            // snapshots survive physical output throttling in the Authority ring.
            if (!TakeDiagnosticLine()) { diag537NextRetryAt = Time.time + 1f; return; }

            joinDiagnosticSlotSeen[slotIndex] = true;
            joinDiagnosticAssignedIds[slotIndex] = slot.assignedPlayerId;
            joinDiagnosticOwnerIds[slotIndex] = ownerId;
            joinDiagnosticSyncedStrokes[slotIndex] = slot.syncedTotalStrokes;
            joinDiagnosticPlayerNames[slotIndex] = slot.playerName;
            joinDiagnosticOwnerNames[slotIndex] = ownerName;
            if (!wasSeen && slot.assignedPlayerId == -1 && ownerId == -1
                && string.IsNullOrEmpty(slot.playerName) && string.IsNullOrEmpty(ownerName)
                && slot.syncedTotalStrokes == 0)
                return;
            WriteDiagnosticLine(FormatSlotDiagnostic(slotIndex, slot.assignedPlayerId,
                ownerId, slot.playerName, ownerName, slot.syncedTotalStrokes, updateCall), 0);
        }

        private void EnsureSlotDiagnosticCapacity()
        {
            if (joinDiagnosticSlotSeen.Length == playerSlots.Length)
                return;

            joinDiagnosticSlotSeen = new bool[playerSlots.Length];
            joinDiagnosticAssignedIds = new int[playerSlots.Length];
            joinDiagnosticOwnerIds = new int[playerSlots.Length];
            joinDiagnosticSyncedStrokes = new int[playerSlots.Length];
            joinDiagnosticPlayerNames = new string[playerSlots.Length];
            joinDiagnosticOwnerNames = new string[playerSlots.Length];
        }

        private void LogRowDiagnosticIfChanged(int slotIndex, int playerId, int syncedStrokes,
            int masterTotal, float percent, int status, bool canConfirmComplete,
            bool hasMasterData, bool externalIncomplete, bool isLocal)
        {
            if (!debugLogEnabled || rowDiagnosticLineCount >= ROW_DIAGNOSTIC_MAX_LINES
                || Time.time < rowDiagnosticNextRetryAt) return;
            EnsureRowDiagnosticCapacity();

            var stable = Utilities.IsValid(masterTotals) && masterTotals.masterTotalsStable;
            var worldStrokes = Utilities.IsValid(masterTotals) ? masterTotals.masterTotalStrokes : 0;
            var flags = (canConfirmComplete ? 1 : 0) + (hasReceivedFirstSync ? 2 : 0)
                + (hasMasterData ? 4 : 0) + (externalIncomplete ? 8 : 0) + (isLocal ? 16 : 0)
                + (Utilities.IsValid(externalProgressSource) ? 32 : 0) + (stable ? 64 : 0);
            var offset = slotIndex * ROW_DIAGNOSTIC_WIDTH;
            if (rowDiagnosticSnapshot[offset] == playerId
                && rowDiagnosticSnapshot[offset + 1] == syncedStrokes
                && rowDiagnosticSnapshot[offset + 2] == masterTotal
                && rowDiagnosticSnapshot[offset + 3] == status
                && rowDiagnosticSnapshot[offset + 4] == flags
                && rowDiagnosticSnapshot[offset + 5] == worldStrokes
                && rowDiagnosticSnapshot[offset + 6] == externalProgressReceived
                && rowDiagnosticSnapshot[offset + 7] == externalProgressTotal
                && rowDiagnosticPercents[slotIndex] == percent) return;

            // Admission precedes formatting and cache updates. Rejection leaves
            // the latest state pending for the next existing render, at most 1/s.
            // This new probe requires the shared #537 gate even in standalone use.
            if (!Diag537Take()) { rowDiagnosticNextRetryAt = Time.time + 1f; return; }
            WriteDiagnosticLine(FormatRowProgressDiagnostic(slotIndex, playerId, syncedStrokes,
                masterTotal, percent, status, flags, worldStrokes,
                externalProgressReceived, externalProgressTotal), 0);
            rowDiagnosticSnapshot[offset] = playerId;
            rowDiagnosticSnapshot[offset + 1] = syncedStrokes;
            rowDiagnosticSnapshot[offset + 2] = masterTotal;
            rowDiagnosticSnapshot[offset + 3] = status;
            rowDiagnosticSnapshot[offset + 4] = flags;
            rowDiagnosticSnapshot[offset + 5] = worldStrokes;
            rowDiagnosticSnapshot[offset + 6] = externalProgressReceived;
            rowDiagnosticSnapshot[offset + 7] = externalProgressTotal;
            rowDiagnosticPercents[slotIndex] = percent;
            rowDiagnosticLineCount++;
        }

        private void EnsureRowDiagnosticCapacity()
        {
            if (rowDiagnosticPercents.Length == playerSlots.Length) return;
            rowDiagnosticSnapshot = new int[playerSlots.Length * ROW_DIAGNOSTIC_WIDTH];
            rowDiagnosticPercents = new float[playerSlots.Length];
        }

        public string FormatRowProgressDiagnostic(int slotNumber, int playerId, int syncedStrokes,
            int masterTotal, float percent, int status, int flags, int worldStrokes,
            int externalReceived, int externalTotal)
        {
            if (!debugLogEnabled) return "";
            // percent is normalized (1 = 100%). Negative status selects the
            // external text branch: -externalProgressStatus; 0..4 = ProgressStatus.
            return "[QvInfoXRow] t=" + Time.time.ToString("F3") + " slot=" + slotNumber
                + " playerId=" + playerId + " syncedTotalStrokes=" + syncedStrokes
                + " masterTotal=" + masterTotal + " percent=" + percent.ToString("F4")
                + " status=" + status + " canConfirmComplete=" + ((flags & 1) != 0)
                + " hasReceivedFirstSync=" + ((flags & 2) != 0)
                + " hasMasterData=" + ((flags & 4) != 0)
                + " HasExternalSyncInProgress=" + ((flags & 8) != 0)
                + " isLocal=" + ((flags & 16) != 0) + " externalSource=" + ((flags & 32) != 0)
                + " masterTotalsStable=" + ((flags & 64) != 0)
                + " masterTotalStrokes=" + worldStrokes
                + " externalReceived=" + externalReceived + " externalTotal=" + externalTotal;
        }

        public string FormatSlotDiagnostic(int slotNumber, int assignedPlayerId,
            int ownerPlayerId, string playerName, string ownerDisplayName, int syncedStrokes, string updateCall)
        {
            if (!debugLogEnabled) return "";
            return "[QvInfoXJoin] slot " + slotNumber
                + " updateCall=" + updateCall + " assignedPlayerId=" + assignedPlayerId
                + " ownerPlayerId=" + ownerPlayerId + " playerName=" + playerName
                + " Owner.displayName=" + ownerDisplayName + " syncedTotalStrokes=" + syncedStrokes;
        }

        public string FormatPlayerLifecycleDiagnostic(float time, string eventName, int playerId, string playerName)
        {
            if (!debugLogEnabled) return "";
            return "[QvInfoXJoin] t=" + time.ToString("F3") + " event=" + eventName
                + " playerId=" + playerId + " playerName=" + playerName;
        }

        private void LogPlayerLifecycleDiagnostic(string eventName, int playerId, string playerName)
        {
            if (debugLogEnabled && TakeDiagnosticLine()) WriteDiagnosticLine(FormatPlayerLifecycleDiagnostic(Time.time, eventName, playerId, playerName), 0);
        }

        private bool HasCurrentMasterTotals()
        {
            var master = Networking.Master;
            if (!Utilities.IsValid(master) || master.isSuspended || !Utilities.IsValid(masterTotals))
                return false;
            return masterTotals.HasCurrentTotals(master.playerId, masterReportFreshAfter);
        }

        #endregion

        #region Update loop

        public void _AutoUpdateTick()
        {
            Diag537Flush();
            warningObservationPending = true;
            RefreshAll();
            SendCustomEventDelayedSeconds(nameof(_AutoUpdateTick), autoUpdateInterval);
        }

        public void _RetryMasterReport()
        {
            masterReportRetryPending = false;
            RefreshAll();
        }

        public void _RequestMasterReport()
        {
            masterReportRequestPending = false;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReportForJoiningPlayer));
        }

        [NetworkCallable]
        public void ReportForJoiningPlayer()
        {
            if (Utilities.IsValid(Networking.LocalPlayer) && Networking.LocalPlayer.isMaster)
                RefreshAll();
        }

        public void _OnManualUpdateButton()
        {
            var elapsed = Time.time - lastManualUpdateTime;
            if (elapsed < manualUpdateCooldown)
                return;

            lastManualUpdateTime = Time.time;
            RefreshAll();
        }

        // 新規参加者をできるだけ早くメインビューに反映したいが、短時間に複数人が参加した場合に
        // RequestSerialization が連発して他プレイヤーの同期を妨げないよう、参加/退出による
        // 更新だけを間引く。手動ボタンのクールダウンは押下後だけに適用する。
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (debugLogEnabled)
                LogPlayerLifecycleDiagnostic("OnPlayerJoined", player.playerId, player.displayName);
            var elapsed = Time.time - lastRosterUpdateTime;
            if (elapsed < manualUpdateCooldown)
                return;

            lastRosterUpdateTime = Time.time;
            RefreshAll(true);
        }

        public override void OnMasterTransferred(VRCPlayerApi newMaster)
        {
            if (debugLogEnabled)
            {
                diag537PreviousMaster = lastObservedMasterId;
                diag537Master = Utilities.IsValid(newMaster) ? newMaster.playerId : -1;
                VRCPlayerApi local = Networking.LocalPlayer;
                diag537LocalId = Utilities.IsValid(local) ? local.playerId : -1;
                diag537MasterAt = Time.time; // same clock as [QvInfoXJoin]
                diag537MasterPending = true;
                Diag537Flush();
            }
            if (debugLogEnabled)
            {
                var playerId = Utilities.IsValid(newMaster) ? newMaster.playerId : -1;
                var playerName = Utilities.IsValid(newMaster) ? newMaster.displayName : "";
                LogPlayerLifecycleDiagnostic("OnMasterClientSwitched", playerId, playerName);
            }
            InvalidateMasterReports();
            lastObservedMasterId = Utilities.IsValid(newMaster) ? newMaster.playerId : -1;
            RefreshAll();
            // Other clients can invalidate during the same millisecond as our
            // first report even if our own freshness threshold is earlier.
            if (Utilities.IsValid(newMaster) && newMaster.isLocal)
                ScheduleMasterReportRetry();
            else
                ScheduleMasterReportRequest();
        }

        public override void OnPlayerSuspendChanged(VRCPlayerApi player)
        {
            // The suspended owner cannot clear its synced slot. Wakeup is also
            // the earliest chance for that owner to publish a new report.
            if (player.isMaster && Utilities.IsValid(worldState))
                worldState._InvalidateMasterTotals();
            if (player.isMaster && Utilities.IsValid(masterTotals))
                masterTotals._InvalidateMasterTotals();

            if (player.isLocal && !player.isSuspended)
            {
                RefreshAll();
            }
            else if (player.isMaster && !player.isSuspended)
            {
                ScheduleMasterReportRequest();
            }
        }

        private void InvalidateMasterReports()
        {
            masterReportFreshAfter = Networking.GetServerTimeInMilliseconds();
            if (Utilities.IsValid(worldState))
                worldState._InvalidateMasterTotals();
            if (Utilities.IsValid(masterTotals))
                masterTotals._InvalidateMasterTotals();
        }

        // 退出してもメイン一覧から消さず、グレーアウトした行として残す (要望対応)。
        // CyanPlayerObjectPool のスロットは退出直後に _OnCleanup で名前等をクリアしてしまうため、
        // その前に VRCPlayerApi から直接 playerId/displayName を読み取って別途記録する。
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // VRChat の既知の制約: OnPlayerLeft 内で先に IsValid 等のチェックを挟むと
            // playerId が -1 として読めてしまうことがあるため、生の値を真っ先に読み取る。
            var playerId = player.playerId;
            var displayName = player.displayName;
            if (debugLogEnabled)
                LogPlayerLifecycleDiagnostic("OnPlayerLeft", playerId, displayName);
            var wasMaster = playerId == lastObservedMasterId || player.isMaster;

            AddOrUpdateDeparted(playerId, displayName);

            if (wasMaster)
            {
                InvalidateMasterReports();
                lastObservedMasterId = -1;
                ScheduleMasterReportRequest();
                RefreshAll();
                return;
            }

            var elapsed = Time.time - lastRosterUpdateTime;
            if (elapsed < manualUpdateCooldown)
                return;

            lastRosterUpdateTime = Time.time;
            RefreshAll();
        }

        private void AddOrUpdateDeparted(int playerId, string displayName)
        {
            for (var i = 0; i < departedCount; i++)
            {
                if (departedPlayerIds[i] == playerId)
                {
                    departedPlayerNames[i] = displayName;
                    return;
                }
            }

            if (departedCount < departedPlayerIds.Length)
            {
                departedPlayerIds[departedCount] = playerId;
                departedPlayerNames[departedCount] = displayName;
                departedCount++;
            }
        }

        // 同じセッション内で再参加した場合、グレーアウト行を通常表示に戻すため記録から外す。
        private void RemoveDeparted(int playerId)
        {
            for (var i = 0; i < departedCount; i++)
            {
                if (departedPlayerIds[i] == playerId)
                {
                    departedCount--;
                    departedPlayerIds[i] = departedPlayerIds[departedCount];
                    departedPlayerNames[i] = departedPlayerNames[departedCount];
                    return;
                }
            }
        }

        private void Update()
        {
            // パネル移動は QvInfoX_GrabHandle 側 (専用の VRC_Pickup オブジェクト、QvKeep_Tool と
            // 同じ方式) が自身の Update()/OnPickup/OnDrop で完結して行うため、ここからは触らない。

            PollDesktopInteraction();
            _RefreshExternalProgressAt(Time.realtimeSinceStartup);

            // Slot deserialization has no callback to this manager. Redraw when
            // report validity, its source slot, or its contents change.
            var hasMasterData = HasCurrentMasterTotals();
            var master = Networking.Master;
            if ((Utilities.IsValid(master) && MasterChanged(lastObservedMasterId, master.playerId))
                || hasMasterData != lastRenderedMasterDataValid
                || (hasMasterData && masterTotals.masterTotalsRevision != lastRenderedMasterReportRevision))
                RefreshAll();

            // フッターのクールダウン表示のみ、軽量に毎フレーム更新する。
            if (!Utilities.IsValid(footerStatusText) || !Utilities.IsValid(manualUpdateButton))
                return;

            var remaining = manualUpdateCooldown - (Time.time - lastManualUpdateTime);
            if (remaining > 0f)
            {
                manualUpdateButton.interactable = false;
                footerStatusText.text = L("更新可能まで", "Ready in", "갱신까지")
                    + " " + Mathf.CeilToInt(remaining) + L("秒", "s", "초");
            }
            else
            {
                manualUpdateButton.interactable = true;
                footerStatusText.text = L("手動更新可能", "Ready to refresh", "수동 갱신 가능");
            }
        }

        private void RefreshAll(bool triggeredByJoin = false)
        {
            // Timestamp sampling and formatting are diagnostic-only. Reuse the scan
            // caches for pens/strokes so measurement never adds another traversal.
            long refreshStarted = 0L;
            double ticksToMilliseconds = 0d;
            if (debugLogEnabled)
            {
                ticksToMilliseconds = 1000d / Stopwatch.Frequency;
                refreshStarted = Stopwatch.GetTimestamp();
            }

            ApplyLanguageLabels();
            var master = Networking.Master;
            if (Utilities.IsValid(master))
            {
                if (MasterChanged(lastObservedMasterId, master.playerId))
                {
                    InvalidateMasterReports();
                    if (master.isLocal)
                        ScheduleMasterReportRetry();
                    else
                        ScheduleMasterReportRequest();
                }
                lastObservedMasterId = master.playerId;
            }

            if (penCount == 0)
                DiscoverPens();

            long scanStarted = 0L;
            if (debugLogEnabled) scanStarted = Stopwatch.GetTimestamp();
            ScanLocalPens();
            double scanMs = 0d;
            if (debugLogEnabled) scanMs = (Stopwatch.GetTimestamp() - scanStarted) * ticksToMilliseconds;

            ReportIfMaster();
            RefreshExternalProgress();
            ReportLocalSyncState();
            if (debugLogEnabled)
                ObserveSlotDiagnostics(triggeredByJoin ? "RefreshAll(join)" : "RefreshAll");

            if (!hasReceivedFirstSync)
            {
                if (HasCurrentMasterTotals())
                    hasReceivedFirstSync = true;
            }

            RefreshExternalProgress();
            UpdateSpeedSamples();
            long tallyStarted = 0L;
            if (debugLogEnabled) tallyStarted = Stopwatch.GetTimestamp();
            BuildAuthorStrokeTally();
            double tallyMs = 0d;
            if (debugLogEnabled) tallyMs = (Stopwatch.GetTimestamp() - tallyStarted) * ticksToMilliseconds;

            RenderPlayerRows();
            RenderTop3();
            RenderDetailView();
            lastRenderedMasterDataValid = HasCurrentMasterTotals();
            if (lastRenderedMasterDataValid)
                lastRenderedMasterReportRevision = masterTotals.masterTotalsRevision;

            // One diagnostic per completed refresh, including refreshes reached from
            // Update on report changes; no new timer, polling or refresh throttle.
            if (debugLogEnabled && TakeDiagnosticLine()) WriteDiagnosticLine("[QvInfoXJoin] RefreshAll trigger=" + (triggeredByJoin ? "join" : "other")
                + " ms=" + ((Stopwatch.GetTimestamp() - refreshStarted) * ticksToMilliseconds).ToString("F2")
                + " scanMs=" + scanMs.ToString("F2") + " tallyMs=" + tallyMs.ToString("F2")
                + " pens=" + penCount + " strokes=" + cachedTotalStrokes + " players=" + VRCPlayerApi.GetPlayerCount(), 0);
        }

        // Poll only the optional progress variables and redraw from cached totals.
        // Completion must not wait for the expensive pen/tally scan in RefreshAll.
        public void _RefreshExternalProgressAt(float now)
        {
            if (now < nextExternalProgressAt) return;
            if (!Utilities.IsValid(externalProgressSource) && externalProgressStatus == 0 && !externalFastSyncEnabled) return;
            nextExternalProgressAt = now + 1f;
            var previousStatus = externalProgressStatus;
            var previousReceived = externalProgressReceived;
            var previousTotal = externalProgressTotal;
            bool previousFastSync = externalFastSyncEnabled;
            int previousWire = externalFastSyncWire;
            int previousGeneration = externalLatestGeneration;
            int previousReportedState = externalReportedState;
            RefreshExternalProgress();
            // Also handles a newly assigned/reused slot when the progress is unchanged.
            if (externalFastSyncEnabled || previousFastSync) ReportLocalSyncState();
            if (externalProgressStatus == previousStatus
                && externalProgressReceived == previousReceived && externalProgressTotal == previousTotal
                && externalFastSyncEnabled == previousFastSync && externalFastSyncWire == previousWire
                && externalLatestGeneration == previousGeneration && externalReportedState == previousReportedState) return;
            RenderPlayerRows();
            RenderTop3();
        }

        private void RefreshExternalProgress()
        {
            // Clear the cached override first so removal or an invalid source restores the old UI.
            externalProgressStatus = 0;
            externalFastSyncEnabled = false;
            externalLatestGeneration = -1;
            externalReportedState = 0;
            if (!Utilities.IsValid(externalProgressSource)) return;
            ReadFastSyncContract();

            var active = externalProgressSource.GetProgramVariable("externalSyncActive");
            var received = externalProgressSource.GetProgramVariable("externalSyncReceived");
            var total = externalProgressSource.GetProgramVariable("externalSyncTotal");
            var complete = externalProgressSource.GetProgramVariable("externalSyncComplete");
            var stalledSeconds = externalProgressSource.GetProgramVariable("externalSyncStalledSeconds");
            var partial = externalProgressSource.GetProgramVariable("externalSyncPartial");
            var failed = externalProgressSource.GetProgramVariable("externalSyncFailed");
            externalProgressStatus = ExternalProgressStatus(active, received, total, complete, stalledSeconds, partial, failed);
            if (externalProgressStatus == 0) return;

            // ExternalProgressStatus checks every value's type before these casts.
            externalProgressReceived = (int)received;
            externalProgressTotal = (int)total;
        }

        private void ReadFastSyncContract()
        {
            object version = externalProgressSource.GetProgramVariable("externalSyncContractVersion");
            object usesFastSync = externalProgressSource.GetProgramVariable("externalSyncUsesFastSync");
            if (version == null || version.GetType() != typeof(int) || (int)version != 1
                || usesFastSync == null || usesFastSync.GetType() != typeof(bool)) return;
            externalFastSyncEnabled = (bool)usesFastSync;
            if (!externalFastSyncEnabled) return;
            object generation = externalProgressSource.GetProgramVariable("externalSyncGeneration");
            object latest = externalProgressSource.GetProgramVariable("externalSyncLatestGeneration");
            object percent = externalProgressSource.GetProgramVariable("externalSyncPercent");
            object state = externalProgressSource.GetProgramVariable("externalSyncState");
            externalFastSyncWire = PackFastSyncProgress(-1, 0, 0);
            if (latest != null && latest.GetType() == typeof(int)) externalLatestGeneration = (int)latest;
            if (generation == null || generation.GetType() != typeof(int)
                || percent == null || percent.GetType() != typeof(int)
                || state == null || state.GetType() != typeof(int)) return;
            externalFastSyncWire = PackFastSyncProgress((int)generation, (int)percent, (int)state);
            externalReportedState = (int)state;
        }

        // Signed int only: bit31 marker, bits30..11 generation, bits10..4 percent,
        // bits3..0 state. 0xFFFFF is reserved; never truncate a generation to fit.
        private static int PackFastSyncProgress(int generation, int percent, int state)
        {
            if (generation < 0 || generation >= 0xFFFFF)
            {
                generation = 0xFFFFF;
                percent = 0;
                state = 0;
            }
            if (state < 0 || state > 9 || percent < 0 || percent > 100)
            {
                state = 0;
                percent = 0;
            }
            if (state != 4 && state != 8 && state != 9) percent = Mathf.Min(99, percent);
            if (state == 7) percent = 0;
            return int.MinValue | (generation << 11) | (percent << 4) | state;
        }

        private static int FastSyncGeneration(int value) { return (value >> 11) & 0xFFFFF; }

        // -1: legacy/unreported, 0..9: wire states, 10: a different/new generation.
        private static int FastSyncRowState(int value, int latestGeneration)
        {
            if (value >= 0) return -1;
            int generation = FastSyncGeneration(value);
            int percent = (value >> 4) & 127;
            int state = value & 15;
            if (generation == 0xFFFFF || state > 9 || percent > 100) return 0;
            if (state == 4 || state == 8 || state == 9)
            {
                if (percent != 100) return 0;
                if (latestGeneration < 0 || latestGeneration >= 0xFFFFF || generation != latestGeneration) return 10;
            }
            else if (state != 0 && state != 1 && state != 5 && state != 6 && state != 7
                && generation != latestGeneration) return 10;
            return state;
        }

        private static float FastSyncRowPercent(int value, int state)
        {
            if (state == 4 || state == 8 || state == 9) return 1f;
            if (state <= 0 || state == 7 || state == 10) return 0f;
            return Mathf.Min(99, (value >> 4) & 127) / 100f;
        }

        private string BuildFastSyncProgressText(int state, bool local)
        {
            if (state < 0) return L("進捗未取得", "Progress not reported", "진행 정보 없음");
            if (state == 0)
            {
                if (local && externalReportedState == 9 && externalLatestGeneration < 0)
                    return L("同期元・配信用データ作成中", "Source · Preparing publication", "동기화 원본 · 배포 준비 중");
                return L("進捗不明", "Progress unknown", "진행 상태 알 수 없음");
            }
            if (state == 1) return L("選択・開始待ち", "Waiting to start", "시작 대기 중");
            if (state == 3) return L("最終反映中", "Applying history", "최종 적용 중");
            if (state == 4) return L("同期完了", "Synced", "동기화 완료");
            if (state == 5) return L("一部のみ・同期未完了", "Partial · Sync incomplete", "일부만 · 동기화 미완료");
            if (state == 6) return L("同期未完了・作り直しに失敗しました（容量等）", "Sync incomplete · Rebuild failed", "동기화 미완료 · 재구축 실패");
            if (state == 7) return L("同期OFF", "Sync OFF", "동기화 OFF");
            if (state == 8) return L("同期元・ローカル保持", "Source · Local history ready", "동기화 원본 · 로컬 보유");
            if (state == 9) return L("同期元・配信用データ作成中", "Source · Preparing publication", "동기화 원본 · 배포 준비 중");
            if (state == 10) return L("新世代を確認中", "Checking latest generation", "새 세대 확인 중");
            if (local && externalProgressStatus != 0) return BuildExternalProgressText();
            return L("FastSync 同期中・残り算出中", "FastSync syncing · ETA calculating", "FastSync 동기화 중 · 남은 시간 계산 중");
        }

        // Pure decision mirrored by ExternalSyncProgressTests (no package-specific type dependency).
        private static int ExternalProgressStatus(object active, object received, object total,
            object complete, object stalledSeconds, object partial = null, object failed = null)
        {
            if (active == null || active.GetType() != typeof(bool)) return 0;
            if (received == null || received.GetType() != typeof(int)) return 0;
            if (total == null || total.GetType() != typeof(int)) return 0;
            if (complete == null || complete.GetType() != typeof(bool)) return 0;
            if (stalledSeconds != null && stalledSeconds.GetType() != typeof(float)) return 0;
            if (failed != null && failed.GetType() == typeof(bool) && (bool)failed)
                return partial != null && partial.GetType() == typeof(bool) && (bool)partial ? 5 : 4;
            if (partial != null && partial.GetType() == typeof(bool) && (bool)partial) return 3;
            if (!(bool)active || (bool)complete) return 0;
            var seconds = stalledSeconds == null ? 0f : (float)stalledSeconds;
            return seconds >= 20f ? 2 : 1;
        }

        private bool HasExternalSyncInProgress(QvInfoX_PlayerSlot slot)
        {
            return externalProgressStatus != 0 && Utilities.IsValid(slot)
                && Utilities.IsValid(slot.Owner) && slot.Owner.isLocal;
        }

        private string BuildExternalProgressText()
        {
            if (externalProgressStatus >= 4)
                return L("同期未完了・作り直しに失敗しました（容量）", "Sync incomplete · Rebuild failed (capacity)", "동기화 미완료 · 재구축 실패 (용량)")
                    + (externalProgressStatus == 5 ? L("・一部のみ", " · partial", " · 일부만") : "");
            return L("同期未完了（FastSync ", "Sync incomplete (FastSync ", "동기화 미완료 (FastSync ")
                + externalProgressReceived + "/" + externalProgressTotal + L("）", ")", ")")
                + (externalProgressStatus == 3 ? L("・一部のみ", " · partial", " · 일부만") : externalProgressStatus == 2
                    ? L("・停止中の可能性", " · Possibly stalled", " · 정지 가능성") : "");
        }

        /// <summary>
        /// 前回の RefreshAll 時点からの経過時間と受信済みストローク数の差分から、
        /// プレイヤーごとの受信速度 (strokes/sec) を推定する。
        /// </summary>
        private void UpdateSpeedSamples()
        {
            var n = playerSlots.Length;

            if (prevSyncedStrokes.Length != n)
            {
                prevSyncedStrokes = new int[n];
                prevSampleTime = new float[n];
                estimatedSpeed = new float[n];
                hasSpeedSample = new bool[n];
                sampledPlayerIds = new int[n];
                zeroSpeedStreak = new int[n];
                warningPreviousStrokes = new int[n];
                warningSampledPlayerIds = new int[n];
                hasWarningBaseline = new bool[n];
                for (var i = 0; i < n; i++)
                {
                    prevSampleTime[i] = -1f;
                    sampledPlayerIds[i] = -1;
                    warningSampledPlayerIds[i] = -1;
                }
            }

            var now = Time.time;
            var warningObservation = warningObservationPending;
            warningObservationPending = false;
            var master = Networking.Master;
            var currentMasterId = Utilities.IsValid(master) ? master.playerId : -1;
            var hasMasterData = HasCurrentMasterTotals();
            var masterTotal = hasMasterData ? masterTotals.masterSyncedStrokes : 0;
            if (sampledMasterId != currentMasterId)
            {
                for (var i = 0; i < zeroSpeedStreak.Length; i++)
                {
                    zeroSpeedStreak[i] = 0;
                    hasWarningBaseline[i] = false;
                }
                sampledMasterId = currentMasterId;
            }

            for (var i = 0; i < n; i++)
            {
                var slot = playerSlots[i];
                if (!Utilities.IsValid(slot) || !slot.IsOwnerValid)
                {
                    estimatedSpeed[i] = 0f;
                    hasSpeedSample[i] = false;
                    prevSampleTime[i] = -1f;
                    sampledPlayerIds[i] = -1;
                    zeroSpeedStreak[i] = 0;
                    hasWarningBaseline[i] = false;
                    warningSampledPlayerIds[i] = -1;
                    continue;
                }

                if (externalFastSyncEnabled || slot.syncedTotalStrokes < 0)
                {
                    estimatedSpeed[i] = 0f;
                    hasSpeedSample[i] = false;
                    prevSampleTime[i] = -1f;
                    sampledPlayerIds[i] = -1;
                    zeroSpeedStreak[i] = 0;
                    hasWarningBaseline[i] = false;
                    warningSampledPlayerIds[i] = -1;
                    continue;
                }

                if (prevSampleTime[i] < 0f || sampledPlayerIds[i] != slot.Owner.playerId
                    || slot.syncedTotalStrokes < prevSyncedStrokes[i])
                {
                    // 初回サンプル: 速度は算出不能
                    estimatedSpeed[i] = 0f;
                    hasSpeedSample[i] = false;
                    zeroSpeedStreak[i] = 0;
                }
                else
                {
                    var dt = now - prevSampleTime[i];
                    var dStrokes = slot.syncedTotalStrokes - prevSyncedStrokes[i];
                    estimatedSpeed[i] = dt > 0.01f ? Mathf.Max(0f, dStrokes / dt) : 0f;
                    hasSpeedSample[i] = dt > 0.01f;
                }

                var eligible = hasMasterData && masterTotal > 0
                    && slot.syncedTotalStrokes > 0 && slot.syncedTotalStrokes < masterTotal;
                var ownerChanged = warningSampledPlayerIds[i] != slot.Owner.playerId;
                var progressed = hasWarningBaseline[i]
                    && slot.syncedTotalStrokes != warningPreviousStrokes[i];
                zeroSpeedStreak[i] = NextZeroSpeedStreak(eligible, hasWarningBaseline[i],
                    !ownerChanged, progressed, warningObservation, zeroSpeedStreak[i]);
                if (!eligible || ownerChanged)
                    hasWarningBaseline[i] = false;

                if (eligible && !hasWarningBaseline[i])
                {
                    warningPreviousStrokes[i] = slot.syncedTotalStrokes;
                    warningSampledPlayerIds[i] = slot.Owner.playerId;
                    hasWarningBaseline[i] = true;
                }
                else if (progressed)
                    warningPreviousStrokes[i] = slot.syncedTotalStrokes;

                prevSyncedStrokes[i] = slot.syncedTotalStrokes;
                prevSampleTime[i] = now;
                sampledPlayerIds[i] = slot.Owner.playerId;
            }
        }

        #endregion

        #region Rendering

        private static bool MasterChanged(int observedId, int currentId)
        {
            return observedId >= 0 && observedId != currentId;
        }

        // 0: unknown, 1: downloading, 2: synced, 3: stalled.
        private static int NextZeroSpeedStreak(bool eligible, bool hasBaseline, bool sameOwner,
            bool progressed, bool timerObservation, int previous)
        {
            if (!eligible || !hasBaseline || !sameOwner || progressed) return 0;
            return timerObservation ? Mathf.Min(previous + 1, 2) : previous;
        }

        private static int ProgressStatus(bool known, bool canConfirmComplete, float percent, float speed, bool sampled, int zeroStreak)
        {
            if (!known) return 0;
            if (percent >= 1f) return canConfirmComplete ? 2 : 4;
            if (percent > 0f && zeroStreak >= 2) return 3;
            return 1;
        }

        private static string ProgressPercentText(bool known, bool canConfirmComplete, float percent)
        {
            if (!known || (percent >= 1f && !canConfirmComplete)) return "—";
            if (percent >= 1f) return "100%";
            return Mathf.Min(99, Mathf.RoundToInt(percent * 100f)) + "%";
        }

        // 0: everyone has a slot, 1: assignment is pending, 2: pool capacity is exceeded.
        private static int UnassignedSlotStatus(int playerCount, int assignedCount, int slotCapacity)
        {
            if (playerCount <= assignedCount) return 0;
            return playerCount > slotCapacity ? 2 : 1;
        }

        private static bool PenArrayMatches(int localPenCount, int[] masterPenStrokes)
        {
            return masterPenStrokes != null && masterPenStrokes.Length == localPenCount;
        }

        private static float SyncProgressPercent(int received, int masterTotal, bool hasMasterData)
        {
            if (!hasMasterData) return 0f;
            return masterTotal <= 0 ? 1f : Mathf.Clamp01((float)received / masterTotal);
        }

        private static float RemainingEta(int total, int received, float speed, bool sampled)
        {
            return sampled && speed > 0.01f && received < total
                ? (total - received) / speed : -1f;
        }

        private static bool HasMatchingAssignedPlayer(int assignedPlayerId, int ownerPlayerId)
        {
            return assignedPlayerId > 0 && assignedPlayerId == ownerPlayerId;
        }

        private void RenderPlayerRows()
        {
            if (rowPool == null || rowPool.Length == 0)
            {
                overallEtaSeconds = -1f;
                overallEtaPending = false;
                return;
            }

            // 報告済みの総ストロークが 0 の場合だけ、同期率を一律 100% とみなす。
            var masterTotal = Utilities.IsValid(masterTotals) ? masterTotals.masterSyncedStrokes : 0;
            var hasMasterData = HasCurrentMasterTotals();
            var hasKnownProgress = hasReceivedFirstSync && hasMasterData;
            var canConfirmComplete = hasMasterData && masterTotals.CanConfirmComplete(
                Networking.GetServerTimeInMilliseconds(), MASTER_TOTAL_MAX_AGE_MS);

            // §3.3 並び順: Warning (赤・上部) -> Downloading (青・上部) -> 未開始/算出中 -> Synced (緑・下部)
            var slotCapacity = playerSlots.Length;
            if (orderBuffer.Length != slotCapacity)
            {
                orderBuffer = new int[slotCapacity];
                sortScoresBuffer = new float[slotCapacity];
                percentsBuffer = new float[slotCapacity];
                resolvedStatuses = new int[slotCapacity];
                resolvedFastStates = new int[slotCapacity];
                resolvedKnown = new bool[slotCapacity];
                resolvedComplete = new bool[slotCapacity];
            }
            var validCount = 0;

            // §3.2: 全員の読込完了までの目安時間。ダウンロード中の中で一番遅い人の ETA を採用する。
            var worstEta = -1f;
            overallEtaPending = false;

            for (int i = 0, n = playerSlots.Length; i < n; i++)
            {
                var slot = playerSlots[i];
                // Inconsistent assignments use the existing pending row until the pool settles.
                if (!Utilities.IsValid(slot) || !slot.IsOwnerValid
                    || !HasMatchingAssignedPlayer(slot.assignedPlayerId, slot.Owner.playerId))
                    continue;

                // 同一セッション内で再参加していた場合、グレーアウト記録から外す。
                RemoveDeparted(slot.assignedPlayerId);

                bool fastSync = externalFastSyncEnabled || slot.syncedTotalStrokes < 0;
                bool local = slot.Owner.isLocal;
                int wire = fastSync && local && externalFastSyncEnabled ? externalFastSyncWire : slot.syncedTotalStrokes;
                int fastState = fastSync ? FastSyncRowState(wire, externalLatestGeneration) : -100;
                // OFF is a local mode choice even before the first directory arrives.
                // It never certifies a generation or enables completion.
                if (fastSync && local && externalFastSyncEnabled && fastState == 0
                    && externalLatestGeneration < 0 && externalReportedState == 7) fastState = 7;
                bool externalIncomplete = !fastSync && HasExternalSyncInProgress(slot);
                float percent = fastSync ? FastSyncRowPercent(wire, fastState) : externalIncomplete
                    ? Mathf.Min(0.99f, SyncProgressPercent(externalProgressReceived, externalProgressTotal, true))
                    : SyncProgressPercent(slot.syncedTotalStrokes, masterTotal, hasMasterData);
                bool known = fastSync ? fastState > 0 && fastState != 7 && fastState != 10 : externalIncomplete || hasKnownProgress;
                bool complete = fastSync ? fastState == 4 || fastState == 8 || fastState == 9 : !externalIncomplete && canConfirmComplete;
                var speed = i < estimatedSpeed.Length ? estimatedSpeed[i] : 0f;
                var sampled = i < hasSpeedSample.Length && hasSpeedSample[i];
                int zeroStreak = i < zeroSpeedStreak.Length ? zeroSpeedStreak[i] : 0;
                int status = fastSync ? (fastState == 5 || fastState == 6 ? 3 : complete ? 2 : known ? 1 : 0)
                    : externalIncomplete ? (externalProgressStatus >= 2 ? 3 : 1)
                    : ProgressStatus(hasKnownProgress, canConfirmComplete, percent, speed, sampled, zeroStreak);
                resolvedFastStates[i] = fastState;
                resolvedKnown[i] = known;
                resolvedComplete[i] = complete;
                resolvedStatuses[i] = status;
                percentsBuffer[i] = percent;

                if (fastSync && !complete && fastState != 7) overallEtaPending = true;
                else if (!fastSync && status == 1 && !externalIncomplete)
                {
                    var eta = RemainingEta(masterTotal, slot.syncedTotalStrokes, speed, sampled);
                    if (eta > worstEta) worstEta = eta;
                }
                else if (externalIncomplete) overallEtaPending = true;

                // ソートスコア計算: 小さいほど上部
                float score;
                if (status == 0)
                    score = 200f; // 算出中
                else if (status == 2)
                    score = 300f + (float)slot.assignedPlayerId; // 完了者は下部
                else if (status == 3)
                    score = 0f + (100f - Mathf.RoundToInt(percent * 100f)); // 警告 (最上部)
                else
                    score = 100f + (100f - Mathf.RoundToInt(percent * 100f)); // ダウンロード中 (上部)

                orderBuffer[validCount] = i;
                sortScoresBuffer[validCount] = score;
                validCount++;
            }

            overallEtaSeconds = overallEtaPending ? -1f : worstEta;

            // 単純挿入ソート
            for (var i = 1; i < validCount; i++)
            {
                var keyIdx = orderBuffer[i];
                var keyScore = sortScoresBuffer[i];
                var j = i - 1;
                while (j >= 0 && sortScoresBuffer[j] > keyScore)
                {
                    orderBuffer[j + 1] = orderBuffer[j];
                    sortScoresBuffer[j + 1] = sortScoresBuffer[j];
                    j--;
                }
                orderBuffer[j + 1] = keyIdx;
                sortScoresBuffer[j + 1] = keyScore;
            }

            var rowIndex = 0;
            for (var i = 0; i < validCount && rowIndex < rowPool.Length; i++)
            {
                var slotIndex = orderBuffer[i];
                var slot = playerSlots[slotIndex];
                bool fastSync = resolvedFastStates[slotIndex] != -100;
                var externalIncomplete = !fastSync && HasExternalSyncInProgress(slot);
                var percent = percentsBuffer[slotIndex];
                var hasRowProgress = resolvedKnown[slotIndex];
                var canConfirmRowComplete = resolvedComplete[slotIndex];
                var isWarning = resolvedStatuses[slotIndex] == 3;

                var row = rowPool[rowIndex];
                if (!Utilities.IsValid(row))
                    continue;

                if (debugLogEnabled)
                    LogRowDiagnosticIfChanged(slotIndex, slot.assignedPlayerId, slot.syncedTotalStrokes,
                        fastSync ? -1 : masterTotal, percent, fastSync ? 100 + resolvedFastStates[slotIndex]
                            : externalIncomplete ? -externalProgressStatus : resolvedStatuses[slotIndex],
                        canConfirmRowComplete, hasMasterData, externalIncomplete, slot.Owner.isLocal);

                // Label (row) 自体ではなく行全体 (HorizontalLayoutGroup の親) を有効化する。
                GetRowContainer(row).SetActive(true);
                row.color = NormalRowColor;
                // Synced playerName can still belong to a previous user of this slot.
                row.text = FitPlayerName(slot.Owner.displayName);
                if (rowIndex < rowAuthoredTexts.Length && Utilities.IsValid(rowAuthoredTexts[rowIndex]))
                {
                    rowAuthoredTexts[rowIndex].text = L("描画", "Drawn", "그린 선") + " "
                        + GetAuthoredStrokeCount(slot.assignedPlayerId) + L("本", " strokes", "개");
                    rowAuthoredTexts[rowIndex].color = MutedRowColor;
                }
                if (rowIndex < rowMetaTexts.Length && Utilities.IsValid(rowMetaTexts[rowIndex]))
                {
                    rowMetaTexts[rowIndex].text = BuildRowMetaText(slot, slotIndex, percent, hasMasterData, canConfirmComplete, masterTotal);
                    rowMetaTexts[rowIndex].color = fastSync || externalIncomplete
                        ? (isWarning ? WarningBarColor : !hasRowProgress ? MutedRowColor
                            : canConfirmRowComplete && percent >= 1f ? SyncedBarColor : DownloadingBarColor)
                        : !hasReceivedFirstSync || !hasMasterData ? MutedRowColor
                        : isWarning ? WarningBarColor : percent >= 1f && canConfirmComplete ? SyncedBarColor : MutedRowColor;
                }

                if (rowIndex < rowProgressBars.Length && Utilities.IsValid(rowProgressBars[rowIndex]))
                {
                    rowProgressBars[rowIndex].fillAmount = hasRowProgress ? Mathf.Clamp01(percent) : 0f;
                    if (!hasRowProgress)
                        rowProgressBars[rowIndex].color = PendingBarColor;
                    else if (isWarning)
                        rowProgressBars[rowIndex].color = WarningBarColor;
                    else if (percent >= 1f && canConfirmRowComplete)
                        rowProgressBars[rowIndex].color = SyncedBarColor;
                    else
                        rowProgressBars[rowIndex].color = DownloadingBarColor;
                }

                if (rowIndex < rowPercentTexts.Length && Utilities.IsValid(rowPercentTexts[rowIndex]))
                {
                    rowPercentTexts[rowIndex].color = !hasRowProgress
                        ? MutedRowColor : isWarning ? WarningBarColor : NormalRowColor;
                    rowPercentTexts[rowIndex].text = fastSync && resolvedFastStates[slotIndex] == 7 ? "OFF"
                        : ProgressPercentText(hasRowProgress, canConfirmRowComplete, percent);
                }

                rowIndex++;
            }

            // The pool can lag behind a join, or be smaller than the instance capacity.
            // Keep these players out of the progress/Warning calculation.
            var playerCount = VRCPlayerApi.GetPlayerCount();
            var missingSlots = Mathf.Max(0, playerCount - validCount);
            if (missingSlots > 0 && rowIndex < rowPool.Length && Utilities.IsValid(rowPool[rowIndex]))
            {
                var outsidePool = UnassignedSlotStatus(playerCount, validCount, playerSlots.Length) == 2;
                var row = rowPool[rowIndex];
                GetRowContainer(row).SetActive(true);
                row.color = MutedRowColor;
                row.text = L("未割当", "Unassigned", "미배정") + " (" + missingSlots + ")";
                if (rowIndex < rowAuthoredTexts.Length && Utilities.IsValid(rowAuthoredTexts[rowIndex]))
                    rowAuthoredTexts[rowIndex].text = "";
                if (rowIndex < rowMetaTexts.Length && Utilities.IsValid(rowMetaTexts[rowIndex]))
                {
                    rowMetaTexts[rowIndex].text = outsidePool
                        ? L("枠外", "Outside pool", "슬롯 초과")
                        : L("スロット待ち", "Awaiting slot", "슬롯 대기 중");
                    rowMetaTexts[rowIndex].color = MutedRowColor;
                }
                if (rowIndex < rowProgressBars.Length && Utilities.IsValid(rowProgressBars[rowIndex]))
                {
                    rowProgressBars[rowIndex].fillAmount = 0f;
                    rowProgressBars[rowIndex].color = PendingBarColor;
                }
                if (rowIndex < rowPercentTexts.Length && Utilities.IsValid(rowPercentTexts[rowIndex]))
                {
                    rowPercentTexts[rowIndex].text = "—";
                    rowPercentTexts[rowIndex].color = MutedRowColor;
                }
                rowIndex++;
            }

            // §3.3: 退出済みプレイヤーをグレーアウトした行として一覧の最下部に残す (要望対応)。
            // 読込率や速度はもう意味を持たないため、名前と描いたストローク数のみを表示する。
            for (var d = 0; d < departedCount && rowIndex < rowPool.Length; d++)
            {
                var row = rowPool[rowIndex];
                if (!Utilities.IsValid(row))
                    continue;

                GetRowContainer(row).SetActive(true);
                row.color = DepartedRowColor;
                row.text = FitPlayerName(departedPlayerNames[d]);
                if (rowIndex < rowAuthoredTexts.Length && Utilities.IsValid(rowAuthoredTexts[rowIndex]))
                {
                    rowAuthoredTexts[rowIndex].text = L("描画", "Drawn", "그린 선") + " "
                        + GetAuthoredStrokeCount(departedPlayerIds[d]) + L("本", " strokes", "개");
                    rowAuthoredTexts[rowIndex].color = DepartedRowColor;
                }
                if (rowIndex < rowMetaTexts.Length && Utilities.IsValid(rowMetaTexts[rowIndex]))
                {
                    rowMetaTexts[rowIndex].text = L("退出済み", "Left", "퇴장");
                    rowMetaTexts[rowIndex].color = DepartedRowColor;
                }

                if (rowIndex < rowProgressBars.Length && Utilities.IsValid(rowProgressBars[rowIndex]))
                {
                    rowProgressBars[rowIndex].fillAmount = 0f;
                    rowProgressBars[rowIndex].color = DepartedRowColor;
                }

                if (rowIndex < rowPercentTexts.Length && Utilities.IsValid(rowPercentTexts[rowIndex]))
                {
                    rowPercentTexts[rowIndex].color = DepartedRowColor;
                    rowPercentTexts[rowIndex].text = "—";
                }

                rowIndex++;
            }

            for (; rowIndex < rowPool.Length; rowIndex++)
            {
                if (Utilities.IsValid(rowPool[rowIndex]))
                    GetRowContainer(rowPool[rowIndex]).SetActive(false);
            }
        }

        private GameObject GetRowContainer(Text text)
        {
            var parent = text.transform.parent;
            return parent.name == "LabelGroup" ? parent.parent.gameObject : parent.gameObject;
        }

        private string BuildRowMetaText(QvInfoX_PlayerSlot slot, int slotIndex, float percent, bool hasMasterData, bool canConfirmComplete, int masterTotal)
        {
            if (slotIndex >= 0 && slotIndex < resolvedFastStates.Length && resolvedFastStates[slotIndex] != -100)
                return BuildFastSyncProgressText(resolvedFastStates[slotIndex], slot.Owner.isLocal);
            if (HasExternalSyncInProgress(slot)) return BuildExternalProgressText();

            if (!hasReceivedFirstSync || !hasMasterData)
                return L("集計中", "Calculating", "집계 중");

            var speed = slotIndex >= 0 && slotIndex < estimatedSpeed.Length ? estimatedSpeed[slotIndex] : 0f;
            var sampled = slotIndex >= 0 && slotIndex < hasSpeedSample.Length && hasSpeedSample[slotIndex];
            var status = resolvedStatuses[slotIndex];
            if (status == 2)
                return L("同期完了", "Synced", "동기화 완료");
            if (status == 4)
                return L("確認中", "Checking", "확인 중");

            if (status == 3)
            {
                return L("進捗停滞", "Sync stalled", "동기화 정체");
            }

            // §3.3: 🔵 Downloading (1〜99%、速度と残り時間)
            // ETA には WorldState のインスタンス総ストローク数を使う。
            var remainingEta = RemainingEta(masterTotal, slot.syncedTotalStrokes, speed, sampled);
            var eta = remainingEta >= 0f ? Mathf.CeilToInt(remainingEta) : -1;
            var etaText = eta >= 0 ? L("残り", "ETA", "남은 시간") + " " + eta
                + L("秒", "s", "초") : L("残り算出中", "ETA calculating", "남은 시간 계산 중");
            return L("同期中", "Syncing", "동기화 중") + $" · {speed:F1}/"
                + L("秒", "s", "초") + " · " + etaText;
        }

        private void RenderTop3()
        {
            if (!Utilities.IsValid(top3Text))
                return;

            // §3.2 ヘッダー: Master 基準の総ストローク数・総頂点数を出す。
            // WorldState が有効な場合は Master が集計した値を使い、未確定の場合は
            // ローカル実測値 (cachedTotalStrokes / cachedTotalVertices) を即座にフォールバック表示することで「算出中...」のまま固まるのを防ぐ。
            var hasMasterData = HasCurrentMasterTotals();
            var totalStrokes = hasMasterData
                ? masterTotals.masterTotalStrokes
                : cachedTotalStrokes;
            var totalVertices = hasMasterData
                ? masterTotals.masterTotalVertices
                : cachedTotalVertices;

            // §3.2: 全員の読込完了までのおおよその目安時間 (RenderPlayerRows が算出)。
            var etaLabel = overallEtaSeconds >= 0f
                ? L("全員の完了目安", "All complete ETA", "전체 완료 예상") + " "
                    + Mathf.CeilToInt(overallEtaSeconds) + L("秒", "s", "초")
                : L("全員の完了目安 —", "All complete ETA —", "전체 완료 예상 —");

            if (overallEtaPending) etaLabel = L("全員の完了目安 算出中", "All complete ETA calculating", "전체 완료 예상 계산 중");
            if (!externalFastSyncEnabled && externalProgressStatus != 0) etaLabel = BuildExternalProgressText();

            top3Text.text = L("総ストローク", "Strokes", "총 스트로크") + " " + totalStrokes
                + "    " + L("総頂点", "Vertices", "총 정점") + " " + totalVertices + "\n"
                + etaLabel + "  ·  " + Mathf.RoundToInt(autoUpdateInterval)
                + L("秒ごとに更新", "s refresh", "초마다 갱신");
        }

        private void RenderDetailView()
        {
            if (detailRowTexts == null || detailRowTexts.Length == 0)
                return;

            var hasMasterData = HasCurrentMasterTotals();
            var canConfirmComplete = hasMasterData && masterTotals.CanConfirmComplete(
                Networking.GetServerTimeInMilliseconds(), MASTER_TOTAL_MAX_AGE_MS);
            var rowIndex = 0;

            for (var i = 0; i < penCount && rowIndex < detailRowTexts.Length; i++)
            {
                var syncer = penSyncerRoots[i].GetComponent<QvPen_LateSync>();
                if (!Utilities.IsValid(syncer) || !Utilities.IsValid(syncer.pen))
                    continue;

                var pen = syncer.pen;
                var localStrokes = cachedPenSyncedStrokes[i];

                // 報告済みのペン総量が 0 の場合のみ 100%。未報告なら算出中とする。
                var master = Networking.Master;
                var penArrayMatches = hasMasterData && Utilities.IsValid(worldState)
                    && Utilities.IsValid(master)
                    && worldState.HasCurrentTotals(master.playerId, masterReportFreshAfter)
                    && PenArrayMatches(penCount, worldState.masterPenStrokes);
                var hasKnownPenData = penArrayMatches && worldState.HasPenTotal(i);
                var percent = SyncProgressPercent(localStrokes,
                    hasKnownPenData ? worldState.masterPenStrokes[i] : 0, hasKnownPenData);

                var penManagerForThis = FindPenManagerForPen(pen);

                var widthLabel = "";
                var inkWidth = 0.001f;
                if (Utilities.IsValid(penManagerForThis))
                {
                    inkWidth = penManagerForThis.inkWidth;
                    widthLabel = "  ·  " + L("線幅", "Width", "선 두께") + $" {inkWidth * 1000f:F1}mm";
                }

                var heldLabel = pen.IsUser ? "  ·  " + L("使用中", "In use", "사용 중") : "";

                // §5.4: インクプレビュー (AutoSetup で生成されたグラデーション/特殊マテリアルテクスチャをそのまま表示)
                // Evaluate(1f) の単色で上書きしてしまうと虹色やグラデーションが赤一色に潰れるため、
                // テクスチャ本来の虹色・グラデーション・特殊マテリアル柄・Alphaを完全忠実に表示する。
                // 欠落・未登録のペンもあるため、行位置ではなく Pen 参照でテクスチャを引き当てる。
                var inkPreviewTex = FindInkPreviewTextureForPen(pen);

                // §5.4: 実際の線幅プレビューバー (太さに応じて高さを変更、グラデーション・特殊マテリアルテクスチャを完全表示。
                // 単色スワッチは廃止し、このプレビュー1つでペン判別を行う)
                if (rowIndex < detailInkLines.Length && Utilities.IsValid(detailInkLines[rowIndex]))
                {
                    detailInkLines[rowIndex].texture = inkPreviewTex;
                    detailInkLines[rowIndex].color = Color.white;
                    var rt = detailInkLines[rowIndex].rectTransform;
                    var barHeight = Mathf.Clamp(inkWidth * 3000f, 2f, 16f);
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, barHeight);
                }

                // §7.4: progress rail and separate numeric percent.
                if (rowIndex < detailProgressBars.Length && Utilities.IsValid(detailProgressBars[rowIndex]))
                {
                    var fillRect = detailProgressBars[rowIndex].rectTransform;
                    fillRect.anchorMax = new Vector2(percent, 1f);
                    detailProgressBars[rowIndex].color = !hasKnownPenData ? PendingBarColor
                        : percent >= 1f && canConfirmComplete ? SyncedBarColor : DownloadingBarColor;
                }

                if (rowIndex < detailPercentTexts.Length && Utilities.IsValid(detailPercentTexts[rowIndex]))
                    detailPercentTexts[rowIndex].text = ProgressPercentText(hasKnownPenData, canConfirmComplete, percent);

                var rowText = detailRowTexts[rowIndex];
                if (Utilities.IsValid(rowText))
                {
                    // Show the whole card; the percent lives in its own right-aligned field.
                    GetRowContainer(rowText).SetActive(true);
                    rowText.text = pen.name;
                }
                if (rowIndex < detailMetaTexts.Length && Utilities.IsValid(detailMetaTexts[rowIndex]))
                    detailMetaTexts[rowIndex].text = L("描画", "Drawn", "그린 선") + " "
                        + cachedPenStrokes[i] + L("本", " strokes", "개") + widthLabel + heldLabel
                        + (hasMasterData && !penArrayMatches
                            ? " · " + L("ペン構成不一致", "Pen list mismatch", "펜 목록 불일치") : "");

                rowIndex++;
            }

            for (; rowIndex < detailRowTexts.Length; rowIndex++)
            {
                if (Utilities.IsValid(detailRowTexts[rowIndex]))
                    GetRowContainer(detailRowTexts[rowIndex]).SetActive(false);
            }
        }

        private QvPen_PenManager FindPenManagerForPen(QvPen_Pen pen)
        {
            for (int i = 0, n = indicators.Length; i < n; i++)
            {
                if (Utilities.IsValid(indicators[i]) && indicators[i].Pen == pen)
                    return indicators[i].PenManager;
            }

            return null;
        }

        private Texture2D FindInkPreviewTextureForPen(QvPen_Pen pen)
        {
            for (int i = 0, n = indicators.Length; i < n; i++)
            {
                if (Utilities.IsValid(indicators[i]) && indicators[i].Pen == pen)
                    return indicators[i].InkPreviewTexture;
            }

            return null;
        }

        #endregion

        #region Tab switching

        public void _ShowMainView()
        {
            if (Utilities.IsValid(mainViewRoot))
                mainViewRoot.SetActive(true);
            if (Utilities.IsValid(detailViewRoot))
                detailViewRoot.SetActive(false);
            if (Utilities.IsValid(mainTabImage))
                mainTabImage.color = ActiveTabColor;
            if (Utilities.IsValid(detailTabImage))
                detailTabImage.color = InactiveTabColor;

            // verticalScrollbar は (見た目上の Scrollbar UI 要素を作っていないため) 常に null。
            // ScrollRect 自身の verticalNormalizedPosition を直接操作すれば Scrollbar 無しでも動く。
            if (Utilities.IsValid(mainScrollRect))
                mainScrollRect.verticalNormalizedPosition = 1f;
        }

        public void _ShowDetailView()
        {
            if (Utilities.IsValid(mainViewRoot))
                mainViewRoot.SetActive(false);
            if (Utilities.IsValid(detailViewRoot))
                detailViewRoot.SetActive(true);
            if (Utilities.IsValid(mainTabImage))
                mainTabImage.color = InactiveTabColor;
            if (Utilities.IsValid(detailTabImage))
                detailTabImage.color = ActiveTabColor;

            if (Utilities.IsValid(detailScrollRect))
                detailScrollRect.verticalNormalizedPosition = 1f;
        }

        #endregion

        #region Indicator toggle

        public void _OnIndicatorToggleChanged()
        {
            // デスクトップでは直接ヒットテストが切り替えた直後に、同じクリックで標準 UI 側も
            // Toggle を反転させる。そのままだと元の値へ戻ってしまうため、決めた値へ戻す。
            // 通知なしで戻す (isOn へ代入するとこのハンドラへ再入してしまう)。1 回処理したら印を外し、
            // 以後の標準 UI だけのクリックを妨げない。
            if (desktopToggleGuarded && IsDesktopGuardActive() && Utilities.IsValid(indicatorToggle))
            {
                desktopToggleGuarded = false;
                if (indicatorToggle.isOn != desktopToggleValue)
                    indicatorToggle.SetIsOnWithoutNotify(desktopToggleValue);
            }

            var value = Utilities.IsValid(indicatorToggle) ? indicatorToggle.isOn : true;
            _SetAllIndicatorsEnabled(value);
        }

        public void _SetAllIndicatorsEnabled(bool value)
        {
            if (Utilities.IsValid(inkPreviewManager)) inkPreviewManager._SetAllEnabled(value);
        }

        public void _OnHoldToggleChanged()
        {
            if (!Utilities.IsValid(holdToggle)) return;
            if (desktopHoldGuarded && IsDesktopGuardActive())
            {
                desktopHoldGuarded = false;
                holdToggle.SetIsOnWithoutNotify(desktopHoldValue);
            }
            if (!holdToggle.interactable || !Utilities.IsValid(holdController))
            {
                _RefreshHoldStatus();
                return;
            }
            holdController._SetEnabled(holdToggle.isOn);
        }

        public void _RefreshHoldStatus()
        {
            var desktop = Utilities.IsValid(holdController) && holdController.desktopMode;
            var available = Utilities.IsValid(holdController) && holdController.eligibleCount > 0;
            if (Utilities.IsValid(holdToggle))
            {
                holdToggle.interactable = available && !desktop;
                holdToggle.SetIsOnWithoutNotify(available && (desktop || holdController.requestedEnabled));
            }
            if (!Utilities.IsValid(holdStatusText)) return;
            var status = L("対応するペンなし", "No compatible pens", "호환 펜 없음");
            if (desktop)
                status = L("デスクトップは常にホールド", "Desktop: always hold", "데스크톱: 항상 홀드");
            else if (available)
            {
                // The checkbox follows the latest request while held targets are pending.
                var state = holdController.requestedEnabled ? "ON" : "OFF";
                if (holdController.pendingCount > 0)
                    state += L("・手放すと反映", " · Applies after release", " · 놓으면 적용");
                else if (holdController.mixedState)
                    state = L("混在（押すとON）", "Mixed (click: ON)", "혼합 (누르면 ON)");
                status = L("QvHold・自分のみ・", "QvHold · Local only · ", "QvHold · 나에게만 적용 · ") + state;
            }
            if (holdStatusText.text != status) holdStatusText.text = status;
        }

        #endregion

        #region Desktop 直接ヒットテスト (QvPalette の _PollDesktopInteraction と同じ方式)

        /// <summary>
        /// VRChat の World Space UI は、デスクトップだと「画面中央のクロスヘアが当たった位置」を
        /// 仮想的なクリック位置として扱う仕組みになっており、狭いボタンを正確に狙うのは
        /// 現実的でない (QvKeep が同じ理由でキー操作を導入しているのと同じ制約)。
        /// そのため Unity 標準の UI クリック経路 (Button.onClick 等) には頼らず、
        /// 頭の向きに飛ばしたレイと各要素の RectTransform を直接突き合わせて判定する
        /// (VR はレーザーポインターで直接押せるため対象外。VRChat 標準の経路のまま)。
        /// </summary>
        private void PollDesktopInteraction()
        {
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer) || localPlayer.IsUserInVR())
                return;

            // 標準 UI のクリックはボタンを離した時に届くため、離してから少しの間まで二重適用を防ぐ。
            if (desktopGuardHeld && !Input.GetMouseButton(0))
            {
                desktopGuardHeld = false;
                desktopGuardUntil = Time.time + DesktopGuardSeconds;
            }

            var isDown = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E);
            if (!isDown)
                return;

            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            var ray = new Ray(head.position, head.rotation * Vector3.forward);

            if (TryHitRect(ray, manualUpdateButtonRect))
            {
                _OnManualUpdateButton();
                return;
            }

            if (TryHitRect(ray, mainTabRect))
            {
                _ShowMainView();
                return;
            }

            if (TryHitRect(ray, detailTabRect))
            {
                _ShowDetailView();
                return;
            }

            if (TryHitRect(ray, indicatorToggleRect))
            {
                if (Utilities.IsValid(indicatorToggle))
                {
                    BeginDesktopGuard();
                    desktopToggleValue = !indicatorToggle.isOn;
                    desktopToggleGuarded = true;
                    indicatorToggle.SetIsOnWithoutNotify(desktopToggleValue);
                    _SetAllIndicatorsEnabled(desktopToggleValue);
                }
                return;
            }

            if (TryHitRect(ray, holdToggleRect))
            {
                if (Utilities.IsValid(holdToggle) && holdToggle.interactable
                    && Utilities.IsValid(holdController))
                {
                    BeginDesktopGuard();
                    desktopHoldValue = !holdToggle.isOn;
                    desktopHoldGuarded = true;
                    holdToggle.SetIsOnWithoutNotify(desktopHoldValue);
                    holdController._SetEnabled(desktopHoldValue);
                }
                return;
            }

            if (TryHitRect(ray, resetPositionButtonRect))
            {
                _ResetPosition();
                return;
            }

            if (TryHitRect(ray, languageButtonRect))
            {
                BeginDesktopGuard();
                desktopLanguageGuarded = true;
                CycleLanguageNow();
                return;
            }

        }

        // 直接ヒットテストで「押すたびに結果が変わる操作」(Indicator 切替・言語切替) を処理した印。
        private const float DesktopGuardSeconds = 0.3f;
        private bool desktopGuardHeld;
        private float desktopGuardUntil = -1f;
        private bool desktopToggleGuarded;
        private bool desktopToggleValue;
        private bool desktopHoldGuarded;
        private bool desktopHoldValue;
        private bool desktopLanguageGuarded;

        private void BeginDesktopGuard()
        {
            desktopToggleGuarded = false;
            desktopHoldGuarded = false;
            desktopLanguageGuarded = false;
            desktopGuardHeld = Input.GetMouseButton(0);
            desktopGuardUntil = Time.time + DesktopGuardSeconds;
        }

        private bool IsDesktopGuardActive()
        {
            return desktopGuardHeld || Time.time <= desktopGuardUntil;
        }

        private bool TryHitRect(Ray ray, RectTransform rt)
        {
            if (!Utilities.IsValid(rt))
                return false;

            var plane = new Plane(rt.forward, rt.position);
            if (!plane.Raycast(ray, out var enter) || enter > 5f)
                return false;

            var hitWorld = ray.GetPoint(enter);
            var local = rt.InverseTransformPoint(hitWorld);
            return rt.rect.Contains(new Vector2(local.x, local.y));
        }

        #endregion
    }
}
