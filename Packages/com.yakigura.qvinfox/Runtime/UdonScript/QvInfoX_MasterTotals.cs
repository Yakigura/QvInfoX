using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QvInfoX
{
    /// <summary>Small, independently serialized Master totals for late joiners.</summary>
    [AddComponentMenu("QvInfoX/QvInfoX Master Totals")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvInfoX_MasterTotals : UdonSharpBehaviour
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
        private bool retryPending;

        private void Start()
        {
            SendCustomEventDelayedSeconds(nameof(_Heartbeat), 10f);
        }

        public void _Heartbeat()
        {
            var local = Networking.LocalPlayer;
            if (hasMasterTotals && Utilities.IsValid(local) && local.isMaster
                && !local.isSuspended && Networking.IsOwner(gameObject))
                RequestSerialization();
            SendCustomEventDelayedSeconds(nameof(_Heartbeat), 10f);
        }

        public bool HasCurrentTotals(int currentMasterId, int freshAfter)
        {
            return currentMasterId >= 0 && hasMasterTotals
                && masterTotalsOwnerId == currentMasterId
                && masterTotalsCapturedAt - freshAfter > 0;
        }

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
            int syncedStrokes, int syncedVertices)
        {
            var local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || !local.isMaster || local.isSuspended) return;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            if (!Networking.IsOwner(gameObject)) return;

            var strokes = Mathf.Max(0, totalStrokes);
            var vertices = Mathf.Max(0, totalVertices);
            var nextSyncedStrokes = Mathf.Max(0, syncedStrokes);
            var nextSyncedVertices = Mathf.Max(0, syncedVertices);
            masterTotalsStable = hasMasterTotals && masterTotalsOwnerId == local.playerId
                && masterSyncedStrokes == nextSyncedStrokes
                && masterSyncedVertices == nextSyncedVertices;
            if (!masterTotalsStable || masterTotalStrokes != strokes || masterTotalVertices != vertices)
                masterTotalsRevision++;
            masterTotalStrokes = strokes;
            masterTotalVertices = vertices;
            masterSyncedStrokes = nextSyncedStrokes;
            masterSyncedVertices = nextSyncedVertices;
            masterTotalsOwnerId = local.playerId;
            masterTotalsCapturedAt = Networking.GetServerTimeInMilliseconds();
            hasMasterTotals = true;
            // Do not gate this small snapshot on IsClogged: sustained pen traffic can
            // otherwise prevent even the first late-joiner total from being requested.
            RequestSerialization();
        }

        public override void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
        {
            if (!result.success && Networking.IsOwner(gameObject) && !retryPending)
            {
                retryPending = true;
                SendCustomEventDelayedSeconds(nameof(_RetrySerialization), 5f);
            }
        }

        public void _RetrySerialization()
        {
            retryPending = false;
            if (Networking.IsOwner(gameObject)) RequestSerialization();
        }
    }
}
