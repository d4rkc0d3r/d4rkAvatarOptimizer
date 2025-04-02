#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System;

using d4rkpl4y3r.AvatarOptimizer.Extensions;
using UnityEngine.Animations;

[CustomEditor(typeof(d4rkTextureAtlas))]
public class d4rkTextureAtlasEditor : Editor
{
    private static string packageRootPath;
    private static readonly string textureAtlasFolder = "TextureAtlas";
    private static string TextureAtlasPath => $"{packageRootPath}/{textureAtlasFolder}";
    private d4rkTextureAtlas settings;
    private GameObject clone;
    private int kernelSize = 5;
    private float ssimThreshold = 0.99f;
    private float qualityValue = 1.0f;
    private Material[] ssimDisplayMaterials;

    private void ClearCaches()
    {
        var fields = GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.Name.StartsWithSimple("cache_"))
            {
                field.GetType().GetMethod("Clear")?.Invoke(field.GetValue(this), null);
            }
        }
    }

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
            ClearCaches();
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

        foreach (var tex in GetReferencedTextures())
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
                        if (GetReferencedMaterials(tex).Count > 0)
                            EditorGUILayout.LabelField("Materials using this texture:");
                        using (new EditorGUI.IndentLevelScope())
                        {
                            foreach (var mat in GetReferencedMaterials(tex))
                            {
                                EditorGUILayout.ObjectField(mat, typeof(Material), false);
                            }
                        }
                        if (GetReferencedMeshes(tex).Count > 0)
                            EditorGUILayout.LabelField("Meshes using this texture:");
                        using (new EditorGUI.IndentLevelScope())
                        {
                            foreach (var mesh in GetReferencedMeshes(tex))
                            {
                                EditorGUILayout.ObjectField(mesh, typeof(Mesh), false);
                            }
                        }
                    }
                    if (error != null)
                    {
                        EditorGUILayout.LabelField($"! {error}", EditorStyles.boldLabel);
                    }
                }
            }
        }

        if (clone == null)
        {
            return;
        }

        using (var check = new EditorGUI.ChangeCheckScope())
        {
            qualityValue = EditorGUILayout.Slider("Quality Value", qualityValue, 0, 1);
            if (check.changed)
            {
                ssimThreshold = Mathf.Lerp(0.75f, 0.99f, MathF.Pow(qualityValue, 0.333f));
            }
        }
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Slider("SSIM Threshold", ssimThreshold, 0, 1);
        }
        ssimDisplayMaterials ??= clone.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().Where(m => m != null &&  m.HasProperty("_MainTex")).ToArray();
        foreach (var material in ssimDisplayMaterials)
        {
            material.SetFloat("_QualityThreshold", ssimThreshold);
        }
    }

    private HashSet<Renderer> GetUsedRenderers()
    {
        if (settings.TryGetComponent<d4rkAvatarOptimizer>(out var optimizer))
        {
            return optimizer.GetUsedComponentsInChildren<Renderer>().ToHashSet();
        }
        return new HashSet<Renderer>(settings.GetComponentsInChildren<Renderer>(true));
    }

    private HashSet<Texture2D> cache_textureReferences = new ();
    private HashSet<Texture2D> GetReferencedTextures()
    {
        if (cache_textureReferences.Count > 0)
        {
            return cache_textureReferences;
        }
        cache_textureReferences = new (GetUsedRenderers()
            .SelectMany(r => r.sharedMaterials)
            .Where(m => m != null && m.HasProperty("_MainTex"))
            .Select(m => m.mainTexture as Texture2D)
            .Where(t => t != null));
        return cache_textureReferences;
    }

    private Dictionary<Texture2D, HashSet<Renderer>> cache_textureRendererReferences = new ();
    private HashSet<Renderer> GetReferencedRenderers(Texture2D tex)
    {
        if (cache_textureRendererReferences.TryGetValue(tex, out var renderers))
        {
            return renderers;
        }
        renderers = new (GetUsedRenderers().Where(r => r.sharedMaterials.Any(m => m != null && m.mainTexture == tex)));
        cache_textureRendererReferences[tex] = renderers;
        return renderers;
    }

    private Dictionary<Texture2D, HashSet<Material>> cache_textureMaterialReferences = new ();
    private HashSet<Material> GetReferencedMaterials(Texture2D tex)
    {
        if (cache_textureMaterialReferences.TryGetValue(tex, out var materials))
        {
            return materials;
        }
        materials = new (GetReferencedRenderers(tex).SelectMany(r => r.sharedMaterials).Where(m => m != null && m.mainTexture == tex));
        cache_textureMaterialReferences[tex] = materials;
        return materials;
    }

    private Dictionary<Texture2D, HashSet<Mesh>> cache_textureMeshReferences = new ();
    private HashSet<Mesh> GetReferencedMeshes(Texture2D tex)
    {
        if (cache_textureMeshReferences.TryGetValue(tex, out var meshes))
        {
            return meshes;
        }
        meshes = new (GetReferencedRenderers(tex).Select(r => r.GetSharedMesh()).Where(m => m != null));
        cache_textureMeshReferences[tex] = meshes;
        return meshes;
    }

    private static HashSet<Texture2D> showMaterialAndMeshReferences = new ();
    private bool ShowMaterialsAndMeshesFoldoutButton(Texture2D tex)
    {
        bool showMaterialsAndMeshes = showMaterialAndMeshReferences.Contains(tex);
        if (GUILayout.Button(showMaterialsAndMeshes ? "v" : ">", GUILayout.Width(20)))
        {
            showMaterialsAndMeshes = !showMaterialsAndMeshes;
            if (showMaterialsAndMeshes)
            {
                showMaterialAndMeshReferences.Add(tex);
            }
            else
            {
                showMaterialAndMeshReferences.Remove(tex);
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
        if (GetReferencedMaterials(tex).Count == 0)
        {
            return $"Texture {tex.name} is not referenced by any material.";
        }
        if (GetReferencedMeshes(tex).Count == 0)
        {
            return $"Texture {tex.name} is not referenced by any mesh.";
        }
        return null;
    }

    private Dictionary<(string, int), Texture2D> cache_uncompressedTextures = new ();
    private Texture2D GetUncompressedTexture(Texture2D tex, int mipLevel)
    {
        var path = AssetDatabase.GetAssetPath(tex);
        if (cache_uncompressedTextures.TryGetValue((path, mipLevel), out var updatedTex))
        {
            return updatedTex;
        }
        if (path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg"))
        {
            byte[] rawData = System.IO.File.ReadAllBytes(path);
            var uncompressedTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, true);
            if (ImageConversion.LoadImage(uncompressedTex, rawData, false))
            {
                int mipLevelToCopy = Mathf.RoundToInt(MathF.Log10(uncompressedTex.width / tex.width) / MathF.Log10(2));
                Debug.Log($"Loaded {path} as raw texture from mip level {mipLevelToCopy}.");
                var texWithMips = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, true);
                texWithMips.SetPixels32(uncompressedTex.GetPixels32(mipLevelToCopy));
                texWithMips.Apply(true);
                texWithMips.name = $"{tex.name}_raw";
                cache_uncompressedTextures[(path, mipLevel)] = texWithMips;
                return texWithMips;
            }
        }
        return cache_uncompressedTextures[(path, mipLevel)] = tex;
    }
    private Texture2D CreateSSIMTexture(Texture2D tex, int mipLevel, bool flip)
    {
        var uncompressedTex = GetUncompressedTexture(tex, mipLevel);
        var descriptor = new RenderTextureDescriptor(uncompressedTex.width, uncompressedTex.height, RenderTextureFormat.RFloat, 0);
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        var ssimRenderTexture = RenderTexture.GetTemporary(descriptor);
        Graphics.SetRenderTarget(ssimRenderTexture);
        GL.Clear(true, true, Color.clear);
        var ssimMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/SSIM"));
        ssimMaterial.SetTexture("_Reference", uncompressedTex);
        ssimMaterial.SetTexture("_Target", uncompressedTex);
        ssimMaterial.SetFloat("_sRGB", 1);
        ssimMaterial.SetFloat("_Derivative", 0);
        ssimMaterial.SetFloat("_NormalMap", 0);
        ssimMaterial.SetFloat("_KernelSize", kernelSize);
        ssimMaterial.SetFloat("_TargetMipBias", mipLevel);
        ssimMaterial.SetFloat("_IgnoreAlpha", 1);
        ssimMaterial.SetFloat("_FlipTarget", flip ? 1 : 0);
        ssimMaterial.SetPass(0);
        var mesh = GetIslandMesh(tex);
        Graphics.DrawMeshNow(mesh, Matrix4x4.identity, 0);

        var ssimTexture = new Texture2D(uncompressedTex.width, uncompressedTex.height, TextureFormat.RFloat, false);
        RenderTexture.active = ssimRenderTexture;
        ssimTexture.ReadPixels(new Rect(0, 0, ssimRenderTexture.width, ssimRenderTexture.height), 0, 0);
        ssimTexture.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(ssimRenderTexture);
        ssimTexture.filterMode = FilterMode.Point;
        ssimTexture.wrapMode = TextureWrapMode.Repeat;
        
        AssetDatabase.CreateAsset(ssimTexture, $"{TextureAtlasPath}/{uncompressedTex.name}_SSIM_mip{mipLevel}.asset");
        ssimTexture.name = $"{uncompressedTex.name}_SSIM_mip{mipLevel}";
        return ssimTexture;
    }

    private Texture2D CreateCoverageMaskTexture(Texture2D tex)
    {
        var descriptor = new RenderTextureDescriptor(tex.width, tex.height, RenderTextureFormat.RFloat, 0);
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.sRGB = false;
        var renderTexture = RenderTexture.GetTemporary(descriptor);
        Graphics.SetRenderTarget(renderTexture);
        GL.Clear(true, true, Color.clear);

        var coverageMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/Write Coverage Mask"));
        coverageMaterial.SetPass(0);
        var referencedMaterials = GetReferencedMaterials(tex);
        foreach (var renderer in GetReferencedRenderers(tex))
        {
            var subMeshIndices = renderer.sharedMaterials.Select((m, i) => (m, i)).Where(m => referencedMaterials.Contains(m.m)).Select(m => m.i).ToArray();
            foreach (var index in subMeshIndices)
            {
                var mesh = renderer.GetSharedMesh();
                var clampedIndex = Mathf.Clamp(index, 0, mesh.subMeshCount - 1);
                Graphics.DrawMeshNow(mesh, renderer.transform.localToWorldMatrix, clampedIndex);
            }
        }

        var coverageTexture = new Texture2D(tex.width, tex.height, TextureFormat.RFloat, false);
        RenderTexture.active = renderTexture;
        coverageTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        coverageTexture.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(renderTexture);
        coverageTexture.filterMode = FilterMode.Point;
        coverageTexture.wrapMode = TextureWrapMode.Repeat;
        coverageTexture.name = $"{tex.name}_CoverageMask";
        AssetDatabase.CreateAsset(coverageTexture, $"{TextureAtlasPath}/{tex.name}_CoverageMask.asset");
        return coverageTexture;
    }

    class UVIsland
    {
        public List<Vector2> uv = new ();
        public List<int> triangles = new ();
        private HashSet<Vector2> uvSet = new ();
        public Bounds aabb = new ();
        public void AddTriangle(Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            if (uv.Count == 0)
            {
                aabb.center = uv1;
                aabb.size = Vector3.zero;
            }
            if (uvSet.Add(uv1))
            {
                uv.Add(uv1);
                triangles.Add(uv.Count - 1);
                aabb.Encapsulate(uv1);
            }
            else
            {
                triangles.Add(uv.IndexOf(uv1));
            }
            if (uvSet.Add(uv2))
            {
                uv.Add(uv2);
                triangles.Add(uv.Count - 1);
                aabb.Encapsulate(uv2);
            }
            else
            {
                triangles.Add(uv.IndexOf(uv2));
            }
            if (uvSet.Add(uv3))
            {
                uv.Add(uv3);
                triangles.Add(uv.Count - 1);
                aabb.Encapsulate(uv3);
            }
            else
            {
                triangles.Add(uv.IndexOf(uv3));
            }
        }
        public bool TryAddTriangle(Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            if (uv.Count == 0 || uvSet.Contains(uv1) || uvSet.Contains(uv2) || uvSet.Contains(uv3))
            {
                AddTriangle(uv1, uv2, uv3);
                return true;
            }
            return false;
        }
        public bool ShouldMergeWith(UVIsland other)
        {
            return uvSet.Intersect(other.uvSet).Any();
        }
        public void MergeWith(UVIsland other)
        {
            for (int i = 0; i < other.triangles.Count; i += 3)
            {
                AddTriangle(other.uv[other.triangles[i]], other.uv[other.triangles[i + 1]], other.uv[other.triangles[i + 2]]);
            }
            other.uv.Clear();
            other.triangles.Clear();
            other.uvSet.Clear();
        }
    }

    private Dictionary<Texture2D, Mesh> cache_islandMeshes = new ();
    private Mesh GetIslandMesh(Texture2D tex)
    {
        if (cache_islandMeshes.TryGetValue(tex, out var islandMesh))
        {
            if (islandMesh != null)
            {
                return islandMesh;
            }
        }

        List<UVIsland> islands = new ();
        var referencedMaterials = GetReferencedMaterials(tex);
        foreach (var renderer in GetReferencedRenderers(tex))
        {
            var mesh = renderer.GetSharedMesh();
            var subMeshIndices = renderer.sharedMaterials
                .Select((m, i) => (m, i))
                .Where(m => referencedMaterials.Contains(m.m))
                .Select(m => Mathf.Clamp(m.i, 0, mesh.subMeshCount - 1))
                .Distinct().ToArray();
            foreach (var index in subMeshIndices)
            {
                var triangles = mesh.GetTriangles(index);
                var uv = mesh.uv;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var uv1 = uv[triangles[i]];
                    var uv2 = uv[triangles[i + 1]];
                    var uv3 = uv[triangles[i + 2]];
                    bool needsNewIsland = true;
                    foreach (var island in islands)
                    {
                        if (island.TryAddTriangle(uv1, uv2, uv3))
                        {
                            needsNewIsland = false;
                            break;
                        }
                    }
                    if (needsNewIsland)
                    {
                        var newIsland = new UVIsland();
                        newIsland.TryAddTriangle(uv1, uv2, uv3);
                        islands.Add(newIsland);
                    }
                }
            }
        }

        // merge islands that are close enough
        for (int i = 0; i < islands.Count; i++)
        {
            for (int j = i + 1; j < islands.Count; j++)
            {
                if (islands[i].ShouldMergeWith(islands[j]))
                {
                    islands[i].MergeWith(islands[j]);
                    islands.RemoveAt(j);
                    j = i;
                    continue;
                }
                float distanceThreshold = 8.2f / tex.width;
                // calculate distance between island aabb rectangles
                var aabb1 = islands[i].aabb;
                var aabb2 = islands[j].aabb;
                float dx = Mathf.Max(0, Mathf.Abs(aabb1.center.x - aabb2.center.x) - (aabb1.extents.x + aabb2.extents.x));
                float dy = Mathf.Max(0, Mathf.Abs(aabb1.center.y - aabb2.center.y) - (aabb1.extents.y + aabb2.extents.y));
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                // check if distance is less than pixel size in UV space
                if (distance < distanceThreshold)
                {
                    // go through vertices of both islands and check if they are close enough to merge
                    bool shouldMerge = false;
                    foreach (var uv1 in islands[i].uv)
                    {
                        foreach (var uv2 in islands[j].uv)
                        {
                            if (Vector2.Distance(uv1, uv2) < distanceThreshold)
                            {
                                shouldMerge = true;
                                break;
                            }
                        }
                        if (shouldMerge)
                        {
                            break;
                        }
                    }
                    if (shouldMerge)
                    {
                        islands[i].MergeWith(islands[j]);
                        islands.RemoveAt(j);
                        j = i;
                        continue;
                    }
                }
            }
        }

        islands.Sort((a, b) =>
        {
            if (a.aabb.center.x < b.aabb.center.x)
            {
                return 1;
            }
            else if (a.aabb.center.x > b.aabb.center.x)
            {
                return -1;
            }
            else
            {
                return 0;
            }
        });

        islandMesh = new ();
        islandMesh.name = $"{tex.name}_IslandMesh";
        List<Vector3> uvList = new ();
        List<Vector3> uv2List = new ();
        List<int> triList = new ();
        int currentIslandID = 1;
        for (int i = 0; i < islands.Count; i++)
        {
            var island = islands[i];
            var islandVertices = new Vector3[island.uv.Count];
            for (int j = 0; j < island.uv.Count; j++)
            {
                islandVertices[j] = new Vector3(island.uv[j].x, island.uv[j].y, currentIslandID);
            }
            triList.AddRange(island.triangles.Select(t => t + uvList.Count));
            uvList.AddRange(islandVertices);
            // find first island from back of the list that is either a copy of this island or a flip
            // check bounds size && then check triangle & vertex count
            // after those pass check if you can find matched vertices both un-flipped and flipped
            // if match the offset & flip bit get written into uv2List
            float distanceThreshold = 0.5f / tex.width;
            for (int j = islands.Count - 1; j > i; j--)
            {
                var otherIsland = islands[j];
                if (Mathf.Abs(island.aabb.size.x - otherIsland.aabb.size.x) > distanceThreshold || Mathf.Abs(island.aabb.size.y - otherIsland.aabb.size.y) > distanceThreshold)
                {
                    continue;
                }
                if (island.triangles.Count != otherIsland.triangles.Count || island.uv.Count != otherIsland.uv.Count)
                {
                    continue;
                }
                bool foundMatch = true;
                var offset = new Vector2(island.aabb.center.x, island.aabb.center.y);
                var otherOffset = new Vector2(otherIsland.aabb.center.x, otherIsland.aabb.center.y);
                for (int k = 0; k < island.uv.Count; k++)
                {
                    if (!otherIsland.uv.Any(v => Vector2.Distance((island.uv[k] - offset) * new Vector2(-1, 1), v - otherOffset) < distanceThreshold))
                    {
                        foundMatch = false;
                        break;
                    }
                }
                if (foundMatch)
                {
                    Vector3 offsetVector = new Vector3(otherIsland.aabb.center.x + island.aabb.center.x, otherIsland.aabb.center.y - island.aabb.center.y, 1);
                    uv2List.AddRange(Enumerable.Repeat(offsetVector, island.uv.Count));
                    break;
                }
                foundMatch = true;
                for (int k = 0; k < island.uv.Count; k++)
                {
                    if (!otherIsland.uv.Any(v => Vector2.Distance(island.uv[k] - offset, v - otherOffset) < distanceThreshold))
                    {
                        foundMatch = false;
                        break;
                    }
                }
                if (foundMatch)
                {
                    Vector3 offsetVector = new Vector3(otherIsland.aabb.center.x - island.aabb.center.x, otherIsland.aabb.center.y - island.aabb.center.y, 0);
                    uv2List.AddRange(Enumerable.Repeat(offsetVector, island.uv.Count));
                    break;
                }
            }
            if (uv2List.Count != uvList.Count)
            {
                uv2List.AddRange(Enumerable.Repeat(new Vector3(island.aabb.center.x * 2, 0, -1), island.uv.Count));
            }
            currentIslandID++;
        }
        islandMesh.vertices = uvList.ToArray();
        islandMesh.triangles = triList.ToArray();
        islandMesh.SetUVs(0, uvList.ToArray());
        islandMesh.SetUVs(1, uv2List.ToArray());
        AssetDatabase.CreateAsset(islandMesh, $"{TextureAtlasPath}/{tex.name}_IslandMesh.asset");
        cache_islandMeshes[tex] = islandMesh;
        return islandMesh;
    }

    private Texture2D CreateIslandIDMap(Texture2D tex)
    {
        var islandMesh = GetIslandMesh(tex);

        var descriptor = new RenderTextureDescriptor(tex.width, tex.height, RenderTextureFormat.RFloat, 0);
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        var renderTexture = RenderTexture.GetTemporary(descriptor);
        Graphics.SetRenderTarget(renderTexture);
        GL.Clear(true, true, Color.clear);

        var islandIDMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/Write Coverage Mask"));
        islandIDMaterial.SetFloat("_WriteIslandID", 1);
        islandIDMaterial.SetPass(0);

        Graphics.DrawMeshNow(islandMesh, Matrix4x4.identity, 0);
        var islandIDTexture = new Texture2D(tex.width, tex.height, TextureFormat.RFloat, false);
        RenderTexture.active = renderTexture;
        islandIDTexture.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        islandIDTexture.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(renderTexture);
        islandIDTexture.filterMode = FilterMode.Point;
        islandIDTexture.wrapMode = TextureWrapMode.Repeat;
        islandIDTexture.name = $"{tex.name}_IslandID";
        AssetDatabase.CreateAsset(islandIDTexture, $"{TextureAtlasPath}/{tex.name}_IslandID.asset");
        return islandIDTexture; 
    }

    private void InitializeClone()
    {
        var textures = GetReferencedTextures().Where(t => IsTextureEligible(t) == null).ToArray();
        var ssimDisplayMaterialMap = new Dictionary<Texture2D, Material>();
        for (int i = 0; i < textures.Length; i++)
        {
            var tex = textures[i];
            var mip1 = CreateSSIMTexture(tex, 1, flip: false);
            var mip2 = CreateSSIMTexture(tex, 2, flip: false);
            var flip = CreateSSIMTexture(tex, 0, flip: true);
            var coverage = CreateCoverageMaskTexture(tex);
            var islandID = CreateIslandIDMap(tex);

            var ssimDisplayMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/SSIM Debug View"));
            ssimDisplayMaterial.SetTexture("_Mip1SSIM", mip1);
            ssimDisplayMaterial.SetTexture("_Mip2SSIM", mip2);
            ssimDisplayMaterial.SetTexture("_FlipSSIM", flip);
            //ssimDisplayMaterial.SetTexture("_CoverageMask", coverage);
            ssimDisplayMaterialMap[tex] = ssimDisplayMaterial;
            AssetDatabase.CreateAsset(ssimDisplayMaterial, $"{TextureAtlasPath}/{tex.name}_SSIM.mat");
            
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"{tex.name} (SSIM Debug View)";
            quad.transform.SetParent(clone.transform);
            quad.transform.localPosition = Vector3.zero + Vector3.right * (i + 1);
            quad.transform.localRotation = Quaternion.Euler(0, 180, 0);
            quad.transform.localScale = Vector3.one;
            quad.GetComponent<Renderer>().sharedMaterial = ssimDisplayMaterial;

            var islandDisplayMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/SSIM Debug View"));
            islandDisplayMaterial.SetTexture("_CoverageMask", islandID);
            islandDisplayMaterial.SetFloat("_ShowCoverageMaskAsColors", 1);
            AssetDatabase.CreateAsset(islandDisplayMaterial, $"{TextureAtlasPath}/{tex.name}_IslandID.mat");
            var islandQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            islandQuad.name = $"{tex.name} (Island ID Debug View)";
            islandQuad.transform.SetParent(clone.transform);
            islandQuad.transform.localPosition = Vector3.zero + Vector3.right * (i + 1) + Vector3.up;
            islandQuad.transform.localRotation = Quaternion.Euler(0, 180, 0);
            islandQuad.transform.localScale = Vector3.one;
            islandQuad.GetComponent<Renderer>().sharedMaterial = islandDisplayMaterial;
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