#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(d4rkTextureAtlas))]
public class d4rkTextureAtlasEditor : Editor
{
    private static d4rkTextureAtlas settings;
    private static GameObject clone;
    private static string packageRootPath;
    private static readonly string textureAtlasFolder = "TextureAtlas";
    private static string TextureAtlasPath => $"{packageRootPath}/{textureAtlasFolder}";
    private static int kernelSize = 5;
    private static float ssimThreshold = 0.99f;
    private static Material[] ssimDisplayMaterials;

    public override void OnInspectorGUI()
    {
        settings = settings == null ? target as d4rkTextureAtlas : settings;
        clone = clone == null ? settings.ssimDebugAvatar : clone;

        var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        packageRootPath = path[..path.LastIndexOf('/')];
        packageRootPath = packageRootPath[..packageRootPath.LastIndexOf('/')];
        if (!AssetDatabase.IsValidFolder(TextureAtlasPath))
        {
            AssetDatabase.CreateFolder(packageRootPath, textureAtlasFolder);
        }
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"<size=20>d4rkpl4y3r's Texture Atlas Creator</size>", new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleCenter });
        EditorGUILayout.LabelField($"v{packageInfo.version}", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.Space();

        if (GUILayout.Button("Clear Texture Atlas Folder"))
        {
            AssetDatabase.DeleteAsset(TextureAtlasPath);
            AssetDatabase.CreateFolder(packageRootPath, textureAtlasFolder);
        }

        settings.ssimDebugAvatar = EditorGUILayout.ObjectField("SSIM Debug Avatar", settings.ssimDebugAvatar, typeof(GameObject), true) as GameObject;

        kernelSize = EditorGUILayout.IntSlider("SSIM Kernel Size", kernelSize, 3, 11);
        if (settings.ssimDebugAvatar == null && GUILayout.Button("Create SSIM Debug Avatar"))
        {
            AssetDatabase.DeleteAsset(TextureAtlasPath);
            AssetDatabase.CreateFolder(packageRootPath, textureAtlasFolder);
            clone = Instantiate(settings.gameObject);
            clone.name = $"{settings.name} (SSIM Debug Avatar)";
            clone.transform.position = settings.transform.position + Vector3.right;
            settings.ssimDebugAvatar = clone;
            InitializeClone();
        }
        else if (settings.ssimDebugAvatar != null && GUILayout.Button("Destroy SSIM Debug Avatar"))
        {
            DestroyImmediate(settings.ssimDebugAvatar);
            settings.ssimDebugAvatar = null;
            clone = null;
        }

        if (clone == null)
        {
            return;
        }

        ssimThreshold = EditorGUILayout.Slider("SSIM Threshold", ssimThreshold, 0, 1);
        ssimDisplayMaterials ??= clone.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().Where(m => m.HasProperty("_MainTex")).ToArray();
        foreach (var material in ssimDisplayMaterials)
        {
            material.SetFloat("_QualityThreshold", ssimThreshold);
        }

        var materials = clone.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().Where(m => m.HasProperty("_MainTex")).ToArray();
        var textures = materials.Select(m => m.mainTexture as Texture2D).Where(t => t != null).Distinct().ToArray();
        foreach (var texture in textures)
        {
            EditorGUILayout.ObjectField(texture, typeof(Texture2D), false);
        }
    }

    private Texture2D CreateSSIMTexture(Texture2D tex, int mipLevel)
    {
        var descriptor = new RenderTextureDescriptor(tex.width, tex.height, RenderTextureFormat.RFloat, 0);
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        var ssimRenderTexture = RenderTexture.GetTemporary(descriptor);
        var ssimMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/SSIM"));
        ssimMaterial.SetTexture("_Reference", tex);
        ssimMaterial.SetTexture("_Target", tex);
        ssimMaterial.SetFloat("_sRGB", 1);
        ssimMaterial.SetFloat("_Derivative", 0);
        ssimMaterial.SetFloat("_NormalMap", 0);
        ssimMaterial.SetFloat("_KernelSize", kernelSize);
        ssimMaterial.SetFloat("_TargetMipBias", mipLevel);
        ssimMaterial.SetFloat("_IgnoreAlpha", 1);
        Graphics.Blit(tex, ssimRenderTexture, ssimMaterial);

        // save render texture to texture2d
        var ssimTexture = new Texture2D(tex.width, tex.height, TextureFormat.RHalf, false);
        RenderTexture.active = ssimRenderTexture;
        ssimTexture.ReadPixels(new Rect(0, 0, ssimRenderTexture.width, ssimRenderTexture.height), 0, 0);
        ssimTexture.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(ssimRenderTexture);
        ssimTexture.filterMode = FilterMode.Point;
        ssimTexture.wrapMode = TextureWrapMode.Repeat;
        
        //EditorUtility.CompressTexture(ssimTexture, TextureFormat.BC4, UnityEditor.TextureCompressionQuality.Best);
        AssetDatabase.CreateAsset(ssimTexture, $"{TextureAtlasPath}/{tex.name}_SSIM_mip{mipLevel}.asset");
        return ssimTexture;
    }

    private void InitializeClone()
    {
        var materials = clone.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Where(m => m.HasProperty("_MainTex")).ToArray();
        var textures = materials.Select(m => m.mainTexture as Texture2D).Where(t => t != null).Distinct().ToArray();
        var ssimDisplayMaterialMap = new Dictionary<Texture2D, Material>();
        for (int i = 0; i < textures.Length; i++)
        {
            var tex = textures[i];
            var mip1 = CreateSSIMTexture(tex, 1);
            var mip2 = CreateSSIMTexture(tex, 2);

            var ssimDisplayMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/SSIM Debug View"));
            ssimDisplayMaterial.SetTexture("_Mip1SSIM", mip1);
            ssimDisplayMaterial.SetTexture("_Mip2SSIM", mip2);
            ssimDisplayMaterialMap[tex] = ssimDisplayMaterial;
            AssetDatabase.CreateAsset(ssimDisplayMaterial, $"{TextureAtlasPath}/{tex.name}_SSIM.mat");
            
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"{tex.name} (SSIM Debug View)";
            quad.transform.SetParent(clone.transform);
            quad.transform.localPosition = Vector3.zero + Vector3.right * (i + 1);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = Vector3.one;
            quad.GetComponent<Renderer>().sharedMaterial = ssimDisplayMaterial;
        }
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null || !mat.HasProperty("_MainTex"))
                {
                    continue;
                }
                var tex = mat.mainTexture as Texture2D;
                if (tex == null)
                {
                    continue;
                }
                if (ssimDisplayMaterialMap.ContainsKey(tex))
                {
                    mats[i] = ssimDisplayMaterialMap[tex];
                }
            }
            renderer.sharedMaterials = mats;
        }
        ssimDisplayMaterials = ssimDisplayMaterialMap.Values.ToArray();
    }
}
#endif