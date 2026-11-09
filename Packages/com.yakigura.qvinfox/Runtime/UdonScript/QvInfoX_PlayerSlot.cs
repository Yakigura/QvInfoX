// ----------------------------------------------------------------------
// QvInfoX - Non-invasive QvPen monitoring tools
// ----------------------------------------------------------------------

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using Cyan.PlayerObjectPool;

#pragma warning disable IDE0090, IDE1006

namespace QvInfoX
{
    /// <summary>
    /// プレイヤー単位の同期状況を保持するスロット。CyanPlayerObjectPool (AutoSetup が自動配置) に
    /// 乗せて、参加プレイヤー1人につき1つ割り当てる。CyanPlayerObjectPoolObject の公式拡張ポイント
    /// (_OnOwnerSet / _OnCleanup) のみを実装し、プール本体は一切改変しない。
    /// 各プレイヤーは自分の所有するスロットのフィールドのみを書き込む。
    /// Master の集計はワールド固定の QvInfoX_WorldState が保持する。
    /// </summary>
    [AddComponentMenu("QvInfoX/QvInfoX Player Slot")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvInfoX_PlayerSlot : CyanPlayerObjectPoolObject
    {
        [UdonSynced]
        public string playerName = "";

        // Native: received late-sync-ON strokes. FastSync: signed packed generation,
        // percent and state (Manager owns the codec). The existing 4-byte wire
        // field is reused; it must not be counted as strokes when negative.
        [UdonSynced]
        public int syncedTotalStrokes = 0;

        [System.NonSerialized]
        public int assignedPlayerId = -1;

        // Set by QvInfoX_Manager at startup. The slot reports its native pool and
        // serialization callbacks, while only the manager decides whether logging
        // is enabled and constructs diagnostics.
        [System.NonSerialized]
        public QvInfoX_Manager diagnosticsManager;

        // Owner は基底の CyanPlayerObjectPoolObject が割り当て済み (プレイヤー退出時は null に戻る)。
        public bool IsOwnerValid => Utilities.IsValid(Owner);
        // CyanPlayerObjectPool: このスロットが (再) 割り当てられた時に全クライアントで呼ばれる。
        public override void _OnOwnerSet()
        {
            if (!Utilities.IsValid(Owner))
                return;

            assignedPlayerId = Owner.playerId;
            playerName = Owner.displayName;
            syncedTotalStrokes = 0;
            RequestSerialization();
            NotifyDiagnostics("_OnOwnerSet");
        }

        // CyanPlayerObjectPool: 元の所有者が退出し、このスロットが無効化される直前に全クライアントで呼ばれる。
        public override void _OnCleanup()
        {
            assignedPlayerId = -1;
            playerName = "";
            syncedTotalStrokes = 0;
            NotifyDiagnostics("_OnCleanup");
        }

        public void _ReportLocalTotals(int totalStrokes)
        {
            if (!Networking.IsOwner(gameObject))
                return;

            syncedTotalStrokes = totalStrokes;
            RequestSerialization();
            NotifyDiagnostics("_ReportLocalTotals");
        }

        public override void OnDeserialization()
        {
            NotifyDiagnostics("OnDeserialization");
        }

        private void NotifyDiagnostics(string updateCall)
        {
            if (Utilities.IsValid(diagnosticsManager) && diagnosticsManager.debugLogEnabled)
                diagnosticsManager._OnSlotDiagnosticChanged(this, updateCall);
        }
    }
}
