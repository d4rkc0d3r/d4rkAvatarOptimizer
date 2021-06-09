#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(d4rkAvatarOptimizer))]
public class d4rkAvatarOptimizerEditor : Editor
{
    private static d4rkAvatarOptimizer t;

    private static bool CanCombineWith(List<SkinnedMeshRenderer> list, SkinnedMeshRenderer candidate)
    {
        return true;
    }

    private static List<List<SkinnedMeshRenderer>> FindPossibleSkinnedMeshMerges(GameObject root)
    {
        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var matchedSkinnedMeshes = new List<List<SkinnedMeshRenderer>>();
        foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
        {
            if (skinnedMeshRenderer == null)
                continue;

            var mesh = skinnedMeshRenderer.sharedMesh;
            if (mesh == null)
                continue;

            bool foundMatch = false;
            foreach (var subList in matchedSkinnedMeshes)
            {
                if (CanCombineWith(subList, skinnedMeshRenderer))
                {
                    subList.Add(skinnedMeshRenderer);
                    foundMatch = true;
                    break;
                }
            }
            if (!foundMatch)
            {
                matchedSkinnedMeshes.Add(new List<SkinnedMeshRenderer> { skinnedMeshRenderer });
            }
        }
        return matchedSkinnedMeshes;
    }

    private static void Optimize(GameObject root)
    {

    }
    
    public override void OnInspectorGUI()
    {
        t = (d4rkAvatarOptimizer)target;

        t.MergeBackFaceCullingWithCullingOff =
            EditorGUILayout.Toggle("Merge Cull Back with Cull Off", t.MergeBackFaceCullingWithCullingOff);
        
        if (GUILayout.Button("Create Optimized Copy"))
        {
            var copy = GameObject.Instantiate(t.gameObject);
            Optimize(copy);
            DestroyImmediate(copy.GetComponent<d4rkAvatarOptimizer>());
            var boneCapsule = copy.GetComponentInChildren<BoneCapsule>(true);
            if (boneCapsule != null)
                DestroyImmediate(boneCapsule);
            copy.name = t.gameObject.name + "(OptimizedCopy)";
            copy.SetActive(true);
            t.gameObject.SetActive(false);
            Selection.objects = new Object[] { copy };
        }

        var matchedSkinnedMeshes = FindPossibleSkinnedMeshMerges(t.gameObject);

        foreach (var mergedMeshes in matchedSkinnedMeshes)
        {
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField(mergedMeshes[0].name);
            foreach (var mesh in mergedMeshes)
            {
                foreach (var material in mesh.sharedMaterials)
                {
                    EditorGUILayout.LabelField("  " + mesh.name + "." + material.name);
                }
            }
        }
    }
}
#endif