#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace d4rkpl4y3r.AvatarOptimizer
{
    internal static class AvatarOptimizationRunner
    {
        private static bool didRunInPlayMode = false;

        [InitializeOnEnterPlayMode]
        private static void OnEnterPlaymodeInEditor(EnterPlayModeOptions options)
        {
            didRunInPlayMode = false;
        }

        public static bool Run(GameObject avatarGameObject)
        {
            var optimizers = avatarGameObject.GetComponentsInChildren<d4rkAvatarOptimizer>(includeInactive: false);
            if (optimizers.Length > 1)
            {
                Debug.LogError($"d4rkAvatarOptimizer skipping avatar {avatarGameObject.name} because multiple optimizer components found on avatar. Remove duplicates before uploading.");
                return false;
            }

            var optimizer = optimizers.Length == 1 ? optimizers[0] : null;
            if (optimizer == null && AvatarOptimizerSettings.DoOptimizeWithDefaultSettingsWhenNoComponent)
            {
                optimizer = avatarGameObject.AddComponent<d4rkAvatarOptimizer>();
                AvatarOptimizerSettings.ApplyDefaults(optimizer);
                optimizer.ApplyAutoSettings();
                optimizer.ApplyOnUpload = true;
                Debug.Log($"d4rkAvatarOptimizer added default optimizer component to avatar {avatarGameObject.name} because \"Always Optimize on Upload\" is enabled.");
            }

            if (optimizer == null)
            {
                Debug.Log($"d4rkAvatarOptimizer skipping avatar {avatarGameObject.name} because no optimizer component found.");
                return true;
            }

            if (!optimizer.ApplyOnUpload)
            {
                Debug.Log($"d4rkAvatarOptimizer skipping avatar {avatarGameObject.name} because \"Apply On Upload\" is disabled on the optimizer component.");
                return true;
            }

            try
            {
                if (Application.isPlaying)
                {
                    if (!AvatarOptimizerSettings.DoOptimizeInPlayMode)
                    {
                        Debug.Log($"d4rkAvatarOptimizer skipping avatar {avatarGameObject.name} because \"Optimize in Play Mode\" is disabled.");
                        return true;
                    }

                    if (didRunInPlayMode)
                    {
                        Debug.LogWarning($"d4rkAvatarOptimizer skipping avatar {avatarGameObject.name} because it has already optimized an avatar in this play session.");
                        return true;
                    }
                }

                didRunInPlayMode = Application.isPlaying;
                Debug.Log($"d4rkAvatarOptimizer optimizing avatar {avatarGameObject.name}.");
                optimizer.Optimize();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}
#endif
