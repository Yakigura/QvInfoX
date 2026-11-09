using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using Yakigura.QvSceneSetup.Editor;

namespace QvInfoX.Editor
{
    public sealed class QvInfoXSceneBuildGate : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 91;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return true;
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (QvSceneRecorder.Find(scene, QvInfoX_SceneInstaller.FeatureId) == null) return true;
                var issues = QvInfoX_SceneInstaller.Validate(scene);
                foreach (var issue in issues) Debug.LogWarning("QvInfoX build gate: " + issue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("QvInfoX build gate: validation could not complete: " + ex.Message);
            }
            return true;
        }
    }
}
