#if UNITY_EDITOR && NDMF_EXISTS
using System;
using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(d4rkpl4y3r.AvatarOptimizer.NDMFBuildHook))]

namespace d4rkpl4y3r.AvatarOptimizer
{
    [RunsOnAllPlatforms]
    internal sealed class NDMFBuildHook : Plugin<NDMFBuildHook>
    {
        public override string DisplayName => "d4rkAvatarOptimizer";
        public override string QualifiedName => "d4rkpl4y3r.d4rkavataroptimizer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .Run("Run d4rkAvatarOptimizer", ctx =>
                {
                    if (!AvatarOptimizationRunner.Run(ctx.AvatarRootObject))
                    {
                        throw new InvalidOperationException("d4rkAvatarOptimizer failed to optimize the avatar.");
                    }
                });
        }

        protected override void OnUnhandledException(System.Exception e)
        {
            Debug.LogException(e);
        }
    }
}
#endif
