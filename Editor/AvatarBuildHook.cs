#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;

namespace d4rkpl4y3r.AvatarOptimizer
{
    [InitializeOnLoad]
    public class AvatarBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -1025;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var optimizer = avatarGameObject.GetComponent<d4rkAvatarOptimizer>();
            if (optimizer == null || !optimizer.OptimizeOnUpload)
                return true;
            try
            {
                optimizer.Optimize();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return false;
            }
        }
    }
}
#endif