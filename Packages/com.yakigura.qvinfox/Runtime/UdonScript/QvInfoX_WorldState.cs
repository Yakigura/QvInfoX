using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QvInfoX
{
    /// <summary>One world-owned snapshot of the current Master's pen totals.</summary>
    [AddComponentMenu("QvInfoX/QvInfoX World State")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvInfoX_WorldState : UdonSharpBehaviour
    {
        [UdonSynced] public bool hasMasterTotals;
        [UdonSynced] public int masterTotalsOwnerId = -1;
        [UdonSynced] public int masterTotalsCapturedAt;
        [UdonSynced] public int masterTotalsRevision;
        [UdonSynced] public bool masterTotalsStable;
        [UdonSynced] public int masterTotalStrokes;
        [UdonSynced] public int masterTotalVertices;
        [UdonSynced] public int masterSyncedStrokes;
        [UdonSynced] public int masterSyncedVertices;
        // Per-pen progress denominators contain only late-sync-ON strokes.
        [UdonSynced] public int[] masterPenStrokes = new int[0];
        private bool serializationRetryPending;

        public bool HasCurrentTotals(int currentMasterId, int freshAfter)
        {
            return currentMasterId >= 0 && hasMasterTotals
                && masterTotalsOwnerId == currentMasterId
                && masterTotalsCapturedAt - freshAfter > 0;
        }

        public bool HasPenTotal(int index)
        {
            return index >= 0 && masterPenStrokes != null && index < masterPenStrokes.Length;
        }

        // A recent heartbeat and an unchanged scan are both needed before a client can
        // claim completion. A delayed Manual snapshot may have an obsolete denominator.
        public bool CanConfirmComplete(int now, int maxAgeMilliseconds)
        {
            var age = now - masterTotalsCapturedAt;
            return masterTotalsStable && age >= 0 && age <= maxAgeMilliseconds;
        }

        public void _InvalidateMasterTotals()
        {
            if (!Networking.IsOwner(gameObject)) return;
            hasMasterTotals = false;
            masterTotalsStable = false;
            masterTotalsOwnerId = -1;
            RequestSerialization();
        }

        public void _ReportMasterTotals(int totalStrokes, int totalVertices,
            int syncedStrokes, int syncedVertices, int[] penStrokes)
        {
            var local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || !local.isMaster || local.isSuspended) return;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            if (!Networking.IsOwner(gameObject)) return;

            var nextStrokes = Mathf.Max(0, totalStrokes);
            var nextVertices = Mathf.Max(0, totalVertices);
            var nextSyncedStrokes = Mathf.Max(0, syncedStrokes);
            var nextSyncedVertices = Mathf.Max(0, syncedVertices);
            var sameTotals = hasMasterTotals && masterTotalsOwnerId == local.playerId
                && masterSyncedStrokes == nextSyncedStrokes
                && masterSyncedVertices == nextSyncedVertices && masterPenStrokes != null
                && penStrokes != null && masterPenStrokes.Length == penStrokes.Length;
            if (sameTotals)
            {
                for (var i = 0; i < penStrokes.Length; i++)
                    if (masterPenStrokes[i] != penStrokes[i]) sameTotals = false;
            }
            masterTotalsStable = sameTotals;
            if (!sameTotals || masterTotalStrokes != nextStrokes || masterTotalVertices != nextVertices)
                masterTotalsRevision++;
            masterTotalStrokes = nextStrokes;
            masterTotalVertices = nextVertices;
            masterSyncedStrokes = nextSyncedStrokes;
            masterSyncedVertices = nextSyncedVertices;
            masterPenStrokes = penStrokes ?? new int[0];
            masterTotalsOwnerId = local.playerId;
            masterTotalsCapturedAt = Networking.GetServerTimeInMilliseconds();
            hasMasterTotals = true;
            TrySerializeTotals();
        }

        private void TrySerializeTotals()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!Networking.IsClogged)
            {
                RequestSerialization();
                return;
            }
            ScheduleSerializationRetry();
        }

        public override void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
        {
            if (!result.success) ScheduleSerializationRetry();
        }

        private void ScheduleSerializationRetry()
        {
            if (serializationRetryPending || !Networking.IsOwner(gameObject)) return;
            serializationRetryPending = true;
            SendCustomEventDelayedSeconds(nameof(_RetrySerialization), 2f);
        }

        public void _RetrySerialization()
        {
            serializationRetryPending = false;
            TrySerializeTotals();
        }
    }
}
