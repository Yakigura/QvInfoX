using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yakigura.QvSceneSetup
{
    [AddComponentMenu("")]
    public sealed class QvSceneInstallRecord : MonoBehaviour
    {
        public string featureId;
        public string packageVersion;
        [TextArea] public string optionsJson;
        public List<GameObject> createdObjects = new List<GameObject>();
        public List<Component> createdComponents = new List<Component>();
        public bool createdComponentInventoryComplete;
        public List<QvScenePropertyChange> changes = new List<QvScenePropertyChange>();
        public List<GameObject> networkObjects = new List<GameObject>();
        public List<string> deletedDuringAdd = new List<string>();
        public List<string> irreversibleChanges = new List<string>();
    }

    [Serializable]
    public sealed class QvScenePropertyChange
    {
        public UnityEngine.Object target;
        public string path;
        public int propertyType;
        public string before;
        public string installed;
        public UnityEngine.Object beforeReference;
        public UnityEngine.Object installedReference;
        public bool hadPrefabOverride;
        public bool removedDuringAdd;
    }
}
