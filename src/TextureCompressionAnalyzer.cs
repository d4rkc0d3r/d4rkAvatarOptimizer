#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using System.Linq;

public class TextureCompressionAnalyzer : EditorWindow
{
    public struct TextureVariant
    {
        public enum Type
        {
            Uncompressed,
            Normal,
            Crunched,
            HighQuality
        };
        public TextureImporterCompression compression;
        public int sizeReductionFactor;
        public int? crunchQuality;
        public TextureVariant(TextureImporterCompression compression, int sizeReductionFactor, int? crunchQuality = null)
        {
            this.compression = compression;
            this.sizeReductionFactor = sizeReductionFactor;
            this.crunchQuality = crunchQuality;
        }
        public override string ToString()
        {
            return (crunchQuality != null ? "Crunched" : compression.ToString())
                + (crunchQuality != null ? $"({crunchQuality})" : "")
                + (sizeReductionFactor > 1 ? "_div" + sizeReductionFactor : "");
        }
        public static TextureVariant? Parse(string str)
        {
            var match = Regex.Match(str, @"^(?<compression>Uncompressed|Compressed|CompressedLQ|CompressedHQ|Crunched)(\((?<crunchQuality>\d+)\))?(?:_div(?<sizeReductionFactor>\d+))?$");
            if (!match.Success)
                return null;
            var compression =
                match.Groups["compression"].Value == "Crunched"
                ? TextureImporterCompression.Compressed
                : (TextureImporterCompression)System.Enum.Parse(typeof(TextureImporterCompression), match.Groups["compression"].Value);
            var crunchQuality = match.Groups["crunchQuality"].Success ? (int?)int.Parse(match.Groups["crunchQuality"].Value) : null;
            var sizeReductionFactor = match.Groups["sizeReductionFactor"].Success ? int.Parse(match.Groups["sizeReductionFactor"].Value) : 1;
            return new TextureVariant(compression, sizeReductionFactor, crunchQuality);
        }
    };

    class TextureImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var textureImporter = assetImporter as TextureImporter;
            var match = Regex.Match(assetPath, @"/Z_IGNORE_([\w()\d]+)_(\d+)px\.(\w+)$");
            var parsedVariant = match.Groups[1].Success ? TextureVariant.Parse(match.Groups[1].Value) : null;
            if (parsedVariant != null)
            {
                var variant = parsedVariant.Value;
                textureImporter.textureCompression = variant.compression;
                textureImporter.crunchedCompression = variant.crunchQuality != null;
                textureImporter.compressionQuality = variant.crunchQuality ?? 100;
                textureImporter.maxTextureSize = int.Parse(match.Groups[2].Value);
                var platformSettings = textureImporter.GetPlatformTextureSettings("DefaultTexturePlatform");
                platformSettings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;
                textureImporter.SetPlatformTextureSettings(platformSettings);
                platformSettings = textureImporter.GetPlatformTextureSettings("Standalone");
                platformSettings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;
                textureImporter.SetPlatformTextureSettings(platformSettings);
                platformSettings = textureImporter.GetPlatformTextureSettings("Android");
                platformSettings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;
                textureImporter.SetPlatformTextureSettings(platformSettings);
            }
        }
    }

    public class TextureQuality
    {
        public enum Metric
        {
            Mip0PSNR,
            Mip1PSNR,
            DerivativePSNR,
        }
        private readonly Dictionary<Metric, (float value, string unit)> data = new Dictionary<Metric, (float value, string unit)>();
        public void SetResult(Metric metric, (float value, string unit) result)
        {
            data[metric] = result;
        }
        public (float value, string unit)? GetResult(Metric metric)
        {
            return data.ContainsKey(metric) ? ((float value, string unit)?)data[metric] : null;
        }
        public IEnumerable<(Metric metric, (float value, string unit) result)> GetResults()
        {
            return System.Enum.GetValues(typeof(Metric)).Cast<Metric>().Where(metric => data.ContainsKey(metric)).Select(metric => (metric, data[metric]));
        }
    }

    string assetRootPath;
    Texture2D texture;
    TextureVariant[] variants = new TextureVariant[]
    {
        new TextureVariant(TextureImporterCompression.Uncompressed, 1),
        new TextureVariant(TextureImporterCompression.CompressedHQ, 1),
        new TextureVariant(TextureImporterCompression.CompressedHQ, 2),
        new TextureVariant(TextureImporterCompression.Compressed, 1),
        new TextureVariant(TextureImporterCompression.Compressed, 1, 100),
        new TextureVariant(TextureImporterCompression.Compressed, 1, 50),
    };
    TextureQuality[] quality = null;

    [MenuItem("Tools/d4rkpl4y3r/Texture Compression Analyzer")]
    static void Init()
    {
        GetWindow(typeof(TextureCompressionAnalyzer));
    }
    
    private bool ObjectField<T>(ref T obj, string label = null, bool allowSceneObjects = false) where T : Object
    {
        var result = EditorGUILayout.ObjectField(label, obj, typeof(T), allowSceneObjects) as T;
        if (result != obj)
        {
            obj = result;
            quality = null;
            return true;
        }
        return false;
    }

    private string GetVariantPath(TextureVariant variant)
    {
        string texturePath = AssetDatabase.GetAssetPath(texture);
        string fileExtension = System.IO.Path.GetExtension(texturePath);
        string texGUID = AssetDatabase.AssetPathToGUID(texturePath);
        var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        int textureResolution = Mathf.Min(textureImporter.maxTextureSize, Mathf.Max(texture.width, texture.height));
        textureResolution /= variant.sizeReductionFactor;
        return $"{assetRootPath}/{texGUID}/Z_IGNORE_{variant}_{textureResolution}px{fileExtension}";
    }

    private string GetAssetBundlePath(TextureVariant variant)
    {
        string texturePath = AssetDatabase.GetAssetPath(texture);
        string fileExtension = System.IO.Path.GetExtension(texturePath);
        string texGUID = AssetDatabase.AssetPathToGUID(texturePath);
        return $"{assetRootPath}/{texGUID}/Z_IGNORE_{variant}.AssetBundle";
    }

    private (float vram, float assetBundle) GetVariantSize(TextureVariant variant)
    {
        string variantPath = GetVariantPath(variant);
        string assetBundlePath = GetAssetBundlePath(variant);
        var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(variantPath);
        var importer = AssetImporter.GetAtPath(variantPath) as TextureImporter;
        if (asset == null || importer == null)
        {
            return (-1, -1);
        }
        float bpp = 8;
        switch (asset.format)
        {
            case TextureFormat.DXT1:
            case TextureFormat.DXT1Crunched:
                bpp = 4;
                break;
            case TextureFormat.DXT5:
            case TextureFormat.DXT5Crunched:
            case TextureFormat.BC7:
            case TextureFormat.BC6H:
                bpp = 8;
                break;
            case TextureFormat.RGBA32:
            case TextureFormat.RGB24:
                bpp = 32;
                break;
            case TextureFormat.RGBAHalf:
                bpp = 64;
                break;
            case TextureFormat.RGBAFloat:
                bpp = 128;
                break;
        }
        float vramSize = asset.width * asset.height * bpp / 8;
        if (importer.mipmapEnabled)
        {
            vramSize *= 1.333333f;
        }
        var fi = new System.IO.FileInfo(assetBundlePath);
        float assetBundleSize = (fi.Exists ? fi.Length : -1);
        return (vramSize, assetBundleSize);
    }

    private float CalculatePSNR(Texture2D reference, Texture2D target, bool sRGB, int mipLevel, bool derivative)
    {
        var mseRenderTexture = new RenderTexture(
            reference.width / (int)Mathf.Pow(2, mipLevel),
            reference.height / (int)Mathf.Pow(2, mipLevel),
            0, RenderTextureFormat.ARGBFloat);
        mseRenderTexture.useMipMap = true;
        mseRenderTexture.autoGenerateMips = false;
        mseRenderTexture.Create();
        var mseMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/MeanSquaredError"));
        mseMaterial.SetTexture("_Reference", reference);
        mseMaterial.SetTexture("_Target", target);
        mseMaterial.SetFloat("_sRGB", sRGB ? 1 : 0);
        mseMaterial.SetFloat("_Derivative", derivative ? 1 : 0);
        Graphics.Blit(reference, mseRenderTexture, mseMaterial);
        mseRenderTexture.GenerateMips();

        var onePixelRenderTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
        var copyMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/Copy"));
        copyMaterial.SetFloat("_MipLevel", mseRenderTexture.mipmapCount);
        Graphics.Blit(mseRenderTexture, onePixelRenderTexture, copyMaterial);

        var onePixelTexture = new Texture2D(1, 1, TextureFormat.RGBAFloat, true);
        RenderTexture.active = onePixelRenderTexture;
        onePixelTexture.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        var color = onePixelTexture.GetPixel(0, 0);

        float mse = 0;
        var rgbOnlyFormats = new TextureFormat[]
        {
            TextureFormat.DXT1,
            TextureFormat.DXT1Crunched,
            TextureFormat.RGB24
        };
        if (rgbOnlyFormats.Contains(reference.format))
        {
            mse = (color.r + color.g + color.b) / 3;
        }
        else
        {
            mse = (color.r + color.g + color.b + color.a) / 4;
        }
        return 20 * Mathf.Log10(1) - 10 * Mathf.Log10(mse);
    }

    private TextureQuality CalculateQualityMetrics(TextureVariant reference, TextureVariant target)
    {
        var refTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GetVariantPath(reference));
        var targetTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GetVariantPath(target));
        var refImporter = AssetImporter.GetAtPath(GetVariantPath(reference)) as TextureImporter;
        if (refTexture == null || targetTexture == null || refImporter == null)
        {
            return null;
        }
        var quality = new TextureQuality();

        float psnr = CalculatePSNR(refTexture, targetTexture, refImporter.sRGBTexture, 0, false);        
        quality.SetResult(TextureQuality.Metric.Mip0PSNR, (psnr, "dB"));

        psnr = CalculatePSNR(refTexture, targetTexture, refImporter.sRGBTexture, 1, false);
        quality.SetResult(TextureQuality.Metric.Mip1PSNR, (psnr, "dB"));

        psnr = CalculatePSNR(refTexture, targetTexture, refImporter.sRGBTexture, 0, true);
        quality.SetResult(TextureQuality.Metric.DerivativePSNR, (psnr, "dB"));

        return quality;
    }

    public string FormatByteSize(float size)
    {
        if (size < 0)
        {
            return "N/A";
        }
        else if (size < 1024)
        {
            return $"{Mathf.FloorToInt(size)} B";
        }
        else if (size < 1024 * 1024)
        {
            return $"{size / 1024:F2} KB";
        }
        else if (size < 1024 * 1024 * 1024)
        {
            return $"{size / 1024 / 1024:F2} MB";
        }
        else
        {
            return $"{size / 1024 / 1024 / 1024:F2} GB";
        }
    }

    public void OnGUI()
    {
        var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        assetRootPath = path.Substring(0, path.LastIndexOf('/'));
        assetRootPath = assetRootPath.Substring(0, assetRootPath.LastIndexOf('/'));
        if (GUILayout.Button("Clear Cache"))
        {
            AssetDatabase.DeleteAsset(assetRootPath + "/TextureAnalyzer");
            quality = null;
        }
        if (!AssetDatabase.IsValidFolder(assetRootPath + "/TextureAnalyzer"))
        {
            AssetDatabase.CreateFolder(assetRootPath, "TextureAnalyzer");
        }
        assetRootPath += "/TextureAnalyzer";

        EditorGUILayout.LabelField("assetRootPath: " + assetRootPath);

        ObjectField(ref texture);

        if (texture == null)
            return;

        string texturePath = AssetDatabase.GetAssetPath(texture);
        string fileExtension = System.IO.Path.GetExtension(texturePath);

        var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (textureImporter == null)
        {
            EditorGUILayout.LabelField("textureImporter is null");
            return;
        }

        string texGUID = AssetDatabase.AssetPathToGUID(texturePath);
        string texFolder = assetRootPath + "/" + texGUID;

        if (GUILayout.Button("Analyze Variants"))
        {
            if (!AssetDatabase.IsValidFolder(texFolder))
            {
                AssetDatabase.CreateFolder(assetRootPath, texGUID);
            }
            var buildMap = new List<AssetBundleBuild>();
            foreach (var variant in variants)
            {
                string variantPath = GetVariantPath(variant);
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(variantPath);
                if (asset == null)
                {
                    AssetDatabase.CopyAsset(texturePath, variantPath);
                    buildMap.Add(new AssetBundleBuild() {
                        assetBundleName = $"Z_IGNORE_{variant}.AssetBundle",
                        assetNames = new string[] { variantPath }
                    });
                }
            }
            if (buildMap.Count > 0)
            {
                BuildPipeline.BuildAssetBundles(texFolder, buildMap.ToArray(), BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
                AssetDatabase.Refresh();
            }
            quality = null;
        }

        EditorGUILayout.LabelField("textureGUID: " + texGUID);
        EditorGUILayout.LabelField("texture.resolution: " + texture.width + "x" + texture.height);
        EditorGUILayout.LabelField("texture.format: " + texture.format);
        EditorGUILayout.LabelField("texture.sRGB: " + textureImporter.sRGBTexture);
        EditorGUILayout.LabelField("texture.mipmap: " + textureImporter.mipmapEnabled);

        var sizes = variants.Select(v => GetVariantSize(v)).ToArray();
        var maxVram = sizes[0].vram;
        var maxAssetBundle = sizes[0].assetBundle;
        if (maxVram < 0 || maxAssetBundle < 0)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("Please click \"Analyze Variants\" button first.", MessageType.Warning);
            return;
        }

        if (quality == null || quality.Length != variants.Length)
        {
            quality = new TextureQuality[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                quality[i] = CalculateQualityMetrics(variants[0], variants[i]);
            }
        }

        var variantsWithQualityResult = Enumerable.Range(0, variants.Length)
            .Select(i => new { variant = variants[i], quality = quality[i] })
            .OrderByDescending(v => v.quality.GetResult(TextureQuality.Metric.Mip0PSNR)?.value ?? 0)
            .ToArray();

        foreach (var tuple in variantsWithQualityResult)
        {
            var variant = tuple.variant;
            var quality = tuple.quality;
            EditorGUILayout.Space(5);
            string variantPath = GetVariantPath(variant);
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(variantPath);
            if (asset == null)
            {
                EditorGUILayout.HelpBox($"Variant {variant} is not analyzed.", MessageType.Warning);
                continue;
            }
            EditorGUILayout.LabelField($"{variant} ({asset.format})");
            EditorGUI.indentLevel++;
            var size = GetVariantSize(variant);
            EditorGUILayout.LabelField($"vramSize: {FormatByteSize(size.vram)} ({(size.vram / maxVram * 100):F2}%)");
            EditorGUILayout.LabelField($"downloadSize: {FormatByteSize(size.assetBundle)} ({(size.assetBundle / maxAssetBundle * 100):F2}%)");
            if (variant.compression != TextureImporterCompression.Uncompressed || variant.sizeReductionFactor != 1)
            {
                foreach(var entry in quality.GetResults())
                {
                    EditorGUILayout.LabelField($"{entry.metric}: {entry.result.value:F2}{entry.result.unit}");
                }
            }
            EditorGUI.indentLevel--;
        }
    }
}
#endif