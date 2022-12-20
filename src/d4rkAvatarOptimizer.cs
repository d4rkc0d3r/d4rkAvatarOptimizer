using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class d4rkAvatarOptimizer : MonoBehaviour
{
    public bool WritePropertiesAsStaticValues = true;
    public bool MergeSkinnedMeshes = true;
    public bool MergeStaticMeshesAsSkinned = true;
    public bool ForceMergeBlendShapeMissMatch = false;
    public bool KeepMaterialPropertyAnimationsSeparate = true;
    public bool MergeDifferentPropertyMaterials = true;
    public bool MergeSameDimensionTextures = true;
    public bool MergeBackFaceCullingWithCullingOff = false;
    public bool MergeDifferentRenderQueue = false;
    public bool DeleteUnusedComponents = true;
    public bool DeleteUnusedGameObjects = true;
    public bool UseRingFingerAsFootCollider = false;
    public bool ProfileTimeUsed = false;
    public bool ShowExcludedTransforms = false;
    public List<Transform> ExcludeTransforms = new List<Transform>();
    public bool ShowMeshAndMaterialMergePreview = true;
    public bool ShowDebugInfo = false;
    public bool DebugShowUnparsableMaterials = true;
    public bool DebugShowUnmergableMaterials = true;
    public bool DebugShowUnmergableTextureMaterials = true;
    public bool DebugShowCrunchedTextures = true;
    public bool DebugShowNonBC5NormalMaps = true;
    public bool DebugShowLockedInMaterials = true;
    public bool DebugShowUnusedComponents = true;
    public bool DebugShowAlwaysDisabledGameObjects = true;
    public bool DebugShowMaterialSwaps = true;
    public bool DebugShowGameObjectsWithToggle = true;
    public bool DebugShowUnmovingBones = false;
}
