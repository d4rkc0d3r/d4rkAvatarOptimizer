using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class d4rkAvatarOptimizer : MonoBehaviour
{
    public bool WritePropertiesAsStaticValues = true;
    public bool MergeSkinnedMeshes = true;
    public bool MergeStaticMeshesAsSkinned = false;
    public bool ForceMergeBlendShapeMissMatch = false;
    public bool KeepMaterialPropertyAnimationsSeparate = false;
    public bool MergeDifferentPropertyMaterials = true;
    public bool MergeSameDimensionTextures = false;
    public bool MergeBackFaceCullingWithCullingOff = false;
    public bool DeleteUnusedComponents = true;
    public bool DeleteUnusedGameObjects = false;
    public bool ProfileTimeUsed = false;
    public bool ShowMeshAndMaterialMergePreview = true;
    public bool ShowDebugInfo = false;
    public bool DebugShowAlwaysDisabledBehaviours = true;
    public bool DebugShowAlwaysDisabledGameObjects = true;
    public bool DebugShowUnparsableMaterials = true;
    public bool DebugShowGameObjectsWithToggle = true;
    public bool DebugShowUnmovingTransforms = false;
}
