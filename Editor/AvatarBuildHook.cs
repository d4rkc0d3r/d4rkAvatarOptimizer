#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
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

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
#if NDMF_EXISTS
            return true;
#else
            return AvatarOptimizationRunner.Run(avatarGameObject);
#endif
        }
    }
}
#endif
