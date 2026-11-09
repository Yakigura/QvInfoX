using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;

namespace QvInfoX
{
    // 旧シーンの配置値を Auto Setup 時に新コンポーネントへ一度だけ移すために保持する。
    public enum QvInfoX_TailAxis { Custom, NegativeY, NegativeZ }

    // QvInfoX の詳細ビュー専用データ。ペン尻の表示は QvInkPreview_Pen が担当する。
    [AddComponentMenu("QvInfoX/QvInfoX Indicator")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvInfoX_Indicator : UdonSharpBehaviour
    {
        [SerializeField] private QvPen_Pen pen;
        [SerializeField] private QvPen_PenManager penManager;
        [SerializeField] private Texture2D inkPreviewTexture;
        [HideInInspector] public float tailDistance = 0.02f;
        [HideInInspector] public float upOffset = 0.05f;
        [HideInInspector] public QvInfoX_TailAxis tailAxis = QvInfoX_TailAxis.Custom;
        [HideInInspector] public Vector3 customTailAxis = Vector3.zero;
        [HideInInspector] public Vector3 positionOffset = Vector3.zero;

        public QvPen_Pen Pen => pen;
        public QvPen_PenManager PenManager => penManager;
        public Texture2D InkPreviewTexture => inkPreviewTexture;
    }
}
