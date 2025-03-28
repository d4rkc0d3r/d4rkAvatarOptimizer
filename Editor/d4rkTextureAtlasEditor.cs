#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System;

using d4rkpl4y3r.AvatarOptimizer.Extensions;

[CustomEditor(typeof(d4rkTextureAtlas))]
public class d4rkTextureAtlasEditor : Editor
{
    private static d4rkTextureAtlas settings;
    private static GameObject clone;
    private static string packageRootPath;
    private static readonly string textureAtlasFolder = "TextureAtlas";
    private static string TextureAtlasPath => $"{packageRootPath}/{textureAtlasFolder}";
    private static int kernelSize = 5;
    private static float ssimThreshold = 0.98f;
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

        var materials = settings.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Where(m => m.HasProperty("_MainTex")).ToArray();
        var textures = materials.Select(m => m.mainTexture as Texture2D).Where(t => t != null).Distinct().ToArray();

        foreach (var tex in textures)
        {
            var error = IsTextureEligible(tex);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                bool showMaterialsAndMeshes = true;
                using (new EditorGUILayout.HorizontalScope())
                {
                    showMaterialsAndMeshes = ShowMaterialsAndMeshesFoldoutButton(tex);
                    EditorGUILayout.ObjectField(tex, typeof(Texture2D), false);
                }
                using (new EditorGUI.IndentLevelScope())
                {
                    if (showMaterialsAndMeshes)
                    {
                        EditorGUILayout.LabelField("Materials using this texture:");
                        using (new EditorGUI.IndentLevelScope())
                        {
                            foreach (var mat in materials.Where(m => m.mainTexture == tex))
                            {
                                EditorGUILayout.ObjectField(mat, typeof(Material), false);
                            }
                        }
                        EditorGUILayout.LabelField("Meshes using this texture:");
                        using (new EditorGUI.IndentLevelScope())
                        {
                            foreach (var mesh in settings.GetComponentsInChildren<Renderer>(true)
                                .Where(r => r.sharedMaterials.Any(m => m != null && m.mainTexture == tex))
                                .Select(r => r.GetSharedMesh())
                                .Where(m => m != null)
                                .Distinct())
                            {
                                EditorGUILayout.ObjectField(mesh, typeof(Mesh), false);
                            }
                        }
                    }
                    if (error != null)
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.LabelField(error, new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleLeft });
                        }
                    }
                }
            }
        }

        if (clone == null)
        {
            return;
        }

        ssimThreshold = EditorGUILayout.Slider("SSIM Threshold", ssimThreshold, 0, 1);
        ssimDisplayMaterials ??= clone.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().Where(m => m != null &&  m.HasProperty("_MainTex")).ToArray();
        foreach (var material in ssimDisplayMaterials)
        {
            material.SetFloat("_QualityThreshold", ssimThreshold);
        }
    }

    private static HashSet<Texture2D> hideMaterialAndMeshReferences = new ();

    private bool ShowMaterialsAndMeshesFoldoutButton(Texture2D tex)
    {
        bool showMaterialsAndMeshes = !hideMaterialAndMeshReferences.Contains(tex);
        if (GUILayout.Button(showMaterialsAndMeshes ? "v" : ">", GUILayout.Width(20)))
        {
            showMaterialsAndMeshes = !showMaterialsAndMeshes;
            if (showMaterialsAndMeshes)
            {
                hideMaterialAndMeshReferences.Remove(tex);
            }
            else
            {
                hideMaterialAndMeshReferences.Add(tex);
            }
        }
        return showMaterialsAndMeshes;
    }

    private string IsTextureEligible(Texture2D tex)
    {
        if (tex == null)
        {
            return "Texture is null";
        }
        var eligibleTextureFormats = new HashSet<TextureFormat>()
        {
            TextureFormat.RGBA32,
            TextureFormat.ARGB32,
            TextureFormat.RGB24,
            TextureFormat.BC7,
            TextureFormat.DXT1,
            TextureFormat.DXT1Crunched,
            TextureFormat.DXT5,
            TextureFormat.DXT5Crunched,
        };
        if (!eligibleTextureFormats.Contains(tex.format))
        {
            return $"Texture format {tex.format} is not eligible. Only {string.Join(", ", eligibleTextureFormats)} are eligible.";
        }
        var path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path))
        {
            return "Texture path is null or empty";
        }
        var eligibleExtensions = new HashSet<string>()
        {
            ".png",
            ".jpg",
            ".jpeg",
        };
        if (!eligibleExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Texture path {path} is not eligible. Only {string.Join(", ", eligibleExtensions)} are eligible.";
        }
        return null;
    }

    private static Dictionary<string, Texture2D> rawTextures = new();

    private Texture2D CreateSSIMTexture(Texture2D tex, int mipLevel)
    {
        var path = AssetDatabase.GetAssetPath(tex);
        if (rawTextures.TryGetValue(path, out var updatedTex))
        {
            tex = updatedTex;
        }
        else
        {
            if (path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg"))
            {
                byte[] pngBytes = System.IO.File.ReadAllBytes(path);
                var rawTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, true);
                if (ImageConversion.LoadImage(rawTex, pngBytes, false))
                {
                    int mipLevelToCopy = Mathf.RoundToInt(MathF.Log10(rawTex.width / tex.width) / MathF.Log10(2));
                    Debug.Log($"Loaded {path} as raw texture from mip level {mipLevelToCopy}.");
                    var texWithMips = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, true);
                    texWithMips.SetPixels32(rawTex.GetPixels32(mipLevelToCopy));
                    texWithMips.Apply(true);
                    texWithMips.name = $"{tex.name}_raw";
                    tex = texWithMips;
                    rawTextures[path] = texWithMips;
                }
                else
                {
                    rawTextures[path] = tex;
                }
            }
            else
            {
                rawTextures[path] = tex;
            }
        }
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