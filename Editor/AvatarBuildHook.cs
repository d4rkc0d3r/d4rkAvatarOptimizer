#if UNITY_EDITOR
#if HAS_IEDITOR_ONLY
using System;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;

namespace d4rkpl4y3r.AvatarOptimizer
{
    [InitializeOnLoad]
    public class AvatarBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        // Modular Avatar is at -25, we want to be after that. However usually vrcsdk removes IEditorOnly at -1024.
        // MA patches that to happen last so we can only be at -15 if MA is installed otherwise our component will be removed before getting run.
        #if MODULAR_AVATAR_EXISTS
        public int callbackOrder => -15;
        #else
        public int callbackOrder => -1025;
        #endif

        static private bool didRunInPlayMode = false;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var optimizer = avatarGameObject.GetComponent<d4rkAvatarOptimizer>();
            if (optimizer == null && AvatarOptimizerSettings.DoOptimizeWithDefaultSettingsWhenNoComponent)
            {
                optimizer = avatarGameObject.AddComponent<d4rkAvatarOptimizer>();
                AvatarOptimizerSettings.ApplyDefaults(optimizer);
                optimizer.ApplyAutoSettings();
                optimizer.ApplyOnUpload = true;
            }
            if (optimizer == null || !optimizer.ApplyOnUpload)
            {
                return true;
            }
            try
            {
                if (Application.isPlaying && didRunInPlayMode)
                {
                    Debug.LogWarning($"Only one avatar can be optimized per play mode session. Skipping optimization of {avatarGameObject.name}");
                    return true;
                }
                didRunInPlayMode = Application.isPlaying;
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
#endif