using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace QvInfoX
{
    /// <summary>Local Yes/No control for the fixed pen pairs wired by Auto Setup.</summary>
    [AddComponentMenu("QvInfoX/QvInfoX Hold Controller")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvInfoX_HoldController : UdonSharpBehaviour
    {
        [Header("入室時の初期状態 / Initial state on entry / 입장 시 초기 상태")]
        [Tooltip("0: シーンの設定のまま（書込みなし・既定） / Keep scene settings (no writes, default) / 씬 설정 유지（쓰기 없음, 기본값）\n1: 保持 ON（Yes） / Hold ON (Yes) / 유지 ON (Yes)\n2: 保持 OFF（No） / Hold OFF (No) / 유지 OFF (No)\n操作後は ON=Yes / OFF=No。元値の保存・復元やプレイヤー設定の永続化はしません。 / After a request: ON=Yes, OFF=No; no restore or persistence. / 조작 후 ON=Yes, OFF=No. 원래 값 복원 및 영구 저장 없음.")]
        [Range(0, 2)] public int initialState = 0;

        // Serialized legacy field: old true means entry ON until setup migrates it to initialState=1.
        // Old false maps to Keep scene settings, preserving its former no-write entry behavior.
        [HideInInspector] public bool initiallyEnabled = false;

        [Header("Editor 対象設定 / Editor target selection")]
        [Tooltip("OFF: 全ての対応ペン / All compatible pens. ON: selectedPens のみ / Selected pens only. 再セットアップで反映。")]
        public bool selectedPensOnly;
        public QvPen_Pen[] selectedPens = new QvPen_Pen[0];

        [HideInInspector] public QvPen_Pen[] targetPens = new QvPen_Pen[0];
        [HideInInspector] public VRCPickup[] targetPickups = new VRCPickup[0];
        [HideInInspector] public QvInfoX_Manager statusReceiver;
        [HideInInspector] public bool requestedEnabled;
        [HideInInspector] public int eligibleCount;
        [HideInInspector] public int appliedCount;
        [HideInInspector] public int pendingCount;
        // Persisted for the Editor's initial UI preview, like the counts above.
        [HideInInspector] public bool mixedState;
        [System.NonSerialized] public bool desktopMode;

        private bool initialized;
        private bool hasRequest;
        // Freeze identity once: a replaced public reference must never become a new target.
        private QvPen_Pen[] pens;
        private VRCPickup[] pickups;
        // 0: untouched scene value, 1: waiting for Yes, 2: Yes applied,
        // 3: waiting for No, 4: No applied. No original AutoHold values are stored.
        private int[] states;
        // A request for the next Update scan, not an SDK delayed-event reservation.
        private bool pendingScanRequired;
        private float nextPendingAt;
        private int reportedEligible = -1;
        private int reportedApplied = -1;
        private int reportedPending = -1;
        private bool reportedEnabled;
        private bool reportedMixed;

        private void Start()
        {
            _Initialize();
        }

        private void Update()
        {
            ProcessPendingAt(Time.time);
        }

        // Resume the retained request (or untouched scene settings), without resetting the entry mode.
        private void OnEnable()
        {
            if (!initialized) return;
            pendingScanRequired = false;
            ProcessTargets(false);
        }

        public void _Initialize()
        {
            if (initialized) return;
            initialized = true;
            var localPlayer = Networking.LocalPlayer;
            desktopMode = Utilities.IsValid(localPlayer) && !localPlayer.IsUserInVR();
            var entry = ResolveInitialState(initialState, initiallyEnabled);
            hasRequest = !desktopMode && entry != 0;
            requestedEnabled = entry == 1;
            var count = targetPens == null || targetPickups == null ? 0 : targetPens.Length;
            if (targetPickups != null && count > targetPickups.Length) count = targetPickups.Length;
            pens = new QvPen_Pen[count];
            pickups = new VRCPickup[count];
            states = new int[count];
            for (var i = 0; i < count; i++)
            {
                var pen = targetPens[i];
                var pickup = targetPickups[i];
                if (!Utilities.IsValid(pen) || !Utilities.IsValid(pickup)) continue;
                if (pen.gameObject != pickup.gameObject) continue;
                var duplicate = false;
                for (var j = 0; j < i; j++)
                    if (pickups[j] == pickup) duplicate = true;
                if (duplicate) continue;
                pens[i] = pen;
                pickups[i] = pickup;
            }
            ProcessTargets(false); // Scene mode only reads current values for the UI.
        }

        public void _SetEnabled(bool value)
        {
            _Initialize();
            if (desktopMode) return;
            hasRequest = true;
            requestedEnabled = value;
            ProcessTargets(false);
        }

        // Compatibility sink for any old delayed callback. Only Update advances scans.
        public void _ProcessPending()
        {
        }

        // The explicit clock input and scan arming are mirrored by SDK-free EditMode tests.
        private void ProcessPendingAt(float now)
        {
            if (!initialized || !pendingScanRequired || now < nextPendingAt) return;
            pendingScanRequired = false;
            ProcessTargets(true);
        }

        // Pure C#: mirrored verbatim and source-checked by HoldControllerTests.
        // Low three bits = next state. 16 = write Yes, 32 = write No.
        private static int HoldTransition(bool requested, bool held, int state)
        {
            if (requested)
            {
                if (state == 2) return 2;
                return held ? 1 : 18;
            }
            if (state == 4) return 4;
            return held ? 3 : 36;
        }

        private static int ResolveInitialState(int configured, bool legacyEnabled)
        {
            if (configured == 1 || configured == 2) return configured;
            return legacyEnabled ? 1 : 0;
        }

        // Non-Yes values display as OFF without normalizing them in Scene mode.
        private static int HoldDisplayState(int eligible, int yesCount)
        {
            if (eligible == 0 || yesCount == 0) return 0;
            return yesCount == eligible ? 1 : 2;
        }

        private void ArmPendingScanAt(float now)
        {
            if (pendingCount == 0) pendingScanRequired = false;
            else if (!pendingScanRequired)
            {
                pendingScanRequired = true;
                nextPendingAt = now + 0.25f;
            }
        }

        private void ProcessTargets(bool pendingOnly)
        {
            eligibleCount = 0;
            appliedCount = 0;
            pendingCount = 0;
            for (var i = 0; i < pickups.Length; i++)
            {
                var pen = pens[i];
                var pickup = pickups[i];
                if (!Utilities.IsValid(pen) || !Utilities.IsValid(pickup)
                    || pen.gameObject != pickup.gameObject)
                {
                    // Lost identity: never write into a replacement.
                    pens[i] = null;
                    pickups[i] = null;
                    states[i] = 0;
                    continue;
                }
                eligibleCount++;
                var state = states[i];
                if (hasRequest && (!pendingOnly || state == 1 || state == 3))
                {
                    var transition = HoldTransition(requestedEnabled, pickup.IsHeld, state);
                    if ((transition & 16) != 0 && pickup.AutoHold != VRC_Pickup.AutoHoldMode.Yes)
                        pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
                    if ((transition & 32) != 0 && pickup.AutoHold != VRC_Pickup.AutoHoldMode.No)
                        pickup.AutoHold = VRC_Pickup.AutoHoldMode.No;
                    state = transition & 7;
                    states[i] = state;
                }
                if (pickup.AutoHold == VRC_Pickup.AutoHoldMode.Yes) appliedCount++;
                if (state == 1 || state == 3) pendingCount++;
            }
            var display = HoldDisplayState(eligibleCount, appliedCount);
            mixedState = display == 2;
            if (!hasRequest) requestedEnabled = display == 1;
            if (eligibleCount == 0) requestedEnabled = false;
            ArmPendingScanAt(Time.time);
            if (reportedEligible == eligibleCount && reportedApplied == appliedCount
                && reportedPending == pendingCount && reportedEnabled == requestedEnabled
                && reportedMixed == mixedState) return;
            reportedEligible = eligibleCount;
            reportedApplied = appliedCount;
            reportedPending = pendingCount;
            reportedEnabled = requestedEnabled;
            reportedMixed = mixedState;
            if (Utilities.IsValid(statusReceiver)) statusReceiver._RefreshHoldStatus();
        }
    }
}
