#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Reflection;

namespace d4rkpl4y3r.AvatarOptimizer
{
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
            public TextureImporterFormat compression;
            public int sizeReductionFactor;
            public int? crunchQuality;
            public TextureVariant(TextureImporterFormat compression, int sizeReductionFactor, int? crunchQuality = null)
            {
                this.compression = compression;
                this.sizeReductionFactor = sizeReductionFactor;
                this.crunchQuality = crunchQuality;
            }
            public override string ToString()
            {
                return compression.ToString()
                    + (crunchQuality != null ? $"({crunchQuality})" : "")
                    + (sizeReductionFactor > 1 ? "_div" + sizeReductionFactor : "");
            }
            public static TextureVariant? Parse(string str)
            {
                var match = Regex.Match(str, @"^(?<compression>[a-zA-Z0-9]+)(\((?<crunchQuality>\d+)\))?(?:_div(?<sizeReductionFactor>\d+))?$");
                if (!match.Success)
                    return null;
                var compression = (TextureImporterFormat)System.Enum.Parse(typeof(TextureImporterFormat), match.Groups["compression"].Value);
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
                var refTextureImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(staticTexture)) as TextureImporter;
                var match = Regex.Match(assetPath, @"/Z_IGNORE_([\w()\d]+)\.(\w+)$");
                var parsedVariant = match.Groups[1].Success ? TextureVariant.Parse(match.Groups[1].Value) : null;
                if (parsedVariant != null && refTextureImporter == null)
                {
                    Debug.LogError($"Reference texture importer for {assetPath} not found.");
                }
                if (parsedVariant != null)
                {
                    var variant = parsedVariant.Value;
                    var platformSettings = textureImporter.GetPlatformTextureSettings("Standalone");
                    platformSettings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;
                    platformSettings.overridden = true;
                    platformSettings.format = variant.compression;
                    platformSettings.compressionQuality = variant.crunchQuality ?? 100;
                    platformSettings.crunchedCompression = variant.crunchQuality != null;
                    platformSettings.maxTextureSize = refTextureImporter.maxTextureSize / variant.sizeReductionFactor;
                    textureImporter.isReadable = true;
                    textureImporter.streamingMipmaps = refTextureImporter.streamingMipmaps;
                    textureImporter.mipmapEnabled = refTextureImporter.mipmapEnabled;
                    textureImporter.wrapMode = refTextureImporter.wrapMode;
                    textureImporter.filterMode = refTextureImporter.filterMode;
                    textureImporter.anisoLevel = refTextureImporter.anisoLevel;
                    textureImporter.textureType = refTextureImporter.textureType;
                    textureImporter.alphaIsTransparency = refTextureImporter.alphaIsTransparency;
                    textureImporter.sRGBTexture = refTextureImporter.sRGBTexture;
                    textureImporter.mipmapFilter = refTextureImporter.mipmapFilter;
                    textureImporter.mipMapsPreserveCoverage = refTextureImporter.mipMapsPreserveCoverage;
                    if (refTextureImporter.textureType == TextureImporterType.NormalMap && 
                        (variant.compression == TextureImporterFormat.BC6H || variant.compression == TextureImporterFormat.DXT1))
                    {
                        textureImporter.textureType = TextureImporterType.Default;
                        textureImporter.sRGBTexture = false;
                    }
                    textureImporter.SetPlatformTextureSettings(platformSettings);
                }
            }
        }

        public class TextureQuality
        {
            public enum Metric
            {
                SSIM_mip0,
                SSIM_mip1,
                PSNR_mip0,
                PSNR_mip1,
                //PSNR_derivative,
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
        Vector2 scrollPosition = Vector2.zero;
        Texture2D texture;
        static Texture2D staticTexture;
        bool showMetricOptions = false;
        TextureQuality.Metric sortMetric = TextureQuality.Metric.SSIM_mip0;
        Dictionary<TextureQuality.Metric, bool> enabledMetrics = new Dictionary<TextureQuality.Metric, bool>()
        {
            { TextureQuality.Metric.PSNR_mip0, false },
            { TextureQuality.Metric.PSNR_mip1, false },
            { TextureQuality.Metric.SSIM_mip0, true },
            { TextureQuality.Metric.SSIM_mip1, true },
        };
        TextureVariant[] variants;
        TextureVariant[] variantsRGBA = new TextureVariant[]
        {
            new TextureVariant(TextureImporterFormat.RGBA32, 1),
            new TextureVariant(TextureImporterFormat.BC7, 1),
            new TextureVariant(TextureImporterFormat.BC7, 2),
            new TextureVariant(TextureImporterFormat.DXT5, 1),
            new TextureVariant(TextureImporterFormat.DXT5Crunched, 1, 100),
            new TextureVariant(TextureImporterFormat.DXT5Crunched, 1, 50),
        };
        TextureVariant[] variantsRGB = new TextureVariant[]
        {
            new TextureVariant(TextureImporterFormat.RGB24, 1),
            new TextureVariant(TextureImporterFormat.BC7, 1),
            new TextureVariant(TextureImporterFormat.BC7, 2),
            new TextureVariant(TextureImporterFormat.DXT1, 1),
            new TextureVariant(TextureImporterFormat.DXT1Crunched, 1, 100),
            new TextureVariant(TextureImporterFormat.DXT1Crunched, 1, 50),
        };
        TextureVariant[] variantsNormal = new TextureVariant[]
        {
            new TextureVariant(TextureImporterFormat.RGBA32, 1),
            new TextureVariant(TextureImporterFormat.BC7, 1),
            new TextureVariant(TextureImporterFormat.BC7, 2),
            new TextureVariant(TextureImporterFormat.BC5, 1),
            new TextureVariant(TextureImporterFormat.BC5, 2),
            new TextureVariant(TextureImporterFormat.BC6H, 1),
            new TextureVariant(TextureImporterFormat.BC6H, 2),
            new TextureVariant(TextureImporterFormat.DXT5, 1),
            new TextureVariant(TextureImporterFormat.DXT5Crunched, 1, 100),
            new TextureVariant(TextureImporterFormat.DXT5Crunched, 1, 50),
            new TextureVariant(TextureImporterFormat.DXT1, 1),
        };
        TextureVariant[] variantsHDR = new TextureVariant[]
        {
            new TextureVariant(TextureImporterFormat.RGBAHalf, 1),
            new TextureVariant(TextureImporterFormat.BC6H, 1),
            new TextureVariant(TextureImporterFormat.BC6H, 2),
            new TextureVariant(TextureImporterFormat.BC7, 1),
            new TextureVariant(TextureImporterFormat.DXT1, 1),
            new TextureVariant(TextureImporterFormat.DXT1Crunched, 1, 100),
            new TextureVariant(TextureImporterFormat.DXT1Crunched, 1, 50),
        };
        TextureQuality[] quality = null;

        [MenuItem("Tools/d4rkpl4y3r/Texture Compression Analyzer")]
        static void Init()
        {
            GetWindow(typeof(TextureCompressionAnalyzer));
        }

        private string GetVariantPath(TextureVariant variant)
        {
            string texturePath = AssetDatabase.GetAssetPath(texture);
            string fileExtension = System.IO.Path.GetExtension(texturePath);
            string texGUID = AssetDatabase.AssetPathToGUID(texturePath);
            var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (textureImporter == null)
            {
                return null;
            }
            return $"{assetRootPath}/{texGUID}/Z_IGNORE_{variant}{fileExtension}";
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
                case TextureFormat.BC5:
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

        private float CalculatePSNR(Texture2D reference, Texture2D target, bool isNormalMap, bool sRGB, int mipLevel, bool derivative)
        {
            var mseRenderTexture = new RenderTexture(
                reference.width / (int)Mathf.Pow(2, mipLevel),
                reference.height / (int)Mathf.Pow(2, mipLevel),
                0, RenderTextureFormat.RFloat);
            mseRenderTexture.useMipMap = true;
            mseRenderTexture.autoGenerateMips = false;
            mseRenderTexture.Create();
            var mseMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/MeanSquaredError"));
            mseMaterial.SetTexture("_Reference", reference);
            mseMaterial.SetTexture("_Target", target);
            mseMaterial.SetFloat("_sRGB", sRGB ? 1 : 0);
            mseMaterial.SetFloat("_Derivative", derivative ? 1 : 0);
            mseMaterial.SetFloat("_NormalMap", isNormalMap ? 1 : 0);
            Graphics.Blit(reference, mseRenderTexture, mseMaterial);
            mseRenderTexture.GenerateMips();

            var onePixelRenderTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);
            var copyMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/Copy"));
            copyMaterial.SetFloat("_MipLevel", mseRenderTexture.mipmapCount);
            Graphics.Blit(mseRenderTexture, onePixelRenderTexture, copyMaterial);

            var onePixelTexture = new Texture2D(1, 1, TextureFormat.RFloat, true);
            RenderTexture.active = onePixelRenderTexture;
            onePixelTexture.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            var mse = onePixelTexture.GetPixel(0, 0).r;

            return 20 * Mathf.Log10(1) - 10 * Mathf.Log10(mse);
        }

        private float CalculateSSIM(Texture2D reference, Texture2D target, bool isNormalMap, bool sRGB, int mipLevel, bool derivative)
        {
            var ssimRenderTexture = new RenderTexture(
                reference.width / (int)Mathf.Pow(2, mipLevel),
                reference.height / (int)Mathf.Pow(2, mipLevel),
                0, RenderTextureFormat.RFloat);
            ssimRenderTexture.useMipMap = true;
            ssimRenderTexture.autoGenerateMips = false;
            ssimRenderTexture.Create();
            var ssimMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/SSIM"));
            ssimMaterial.SetTexture("_Reference", reference);
            ssimMaterial.SetTexture("_Target", target);
            ssimMaterial.SetFloat("_sRGB", sRGB ? 1 : 0);
            ssimMaterial.SetFloat("_Derivative", derivative ? 1 : 0);
            ssimMaterial.SetFloat("_NormalMap", isNormalMap ? 1 : 0);
            ssimMaterial.SetFloat("_KernelSize", 11);
            Graphics.Blit(reference, ssimRenderTexture, ssimMaterial);
            ssimRenderTexture.GenerateMips();

            var onePixelRenderTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);
            var copyMaterial = new Material(Shader.Find("d4rkpl4y3r/TextureAnalyzer/Copy"));
            copyMaterial.SetFloat("_MipLevel", ssimRenderTexture.mipmapCount);
            Graphics.Blit(ssimRenderTexture, onePixelRenderTexture, copyMaterial);

            var onePixelTexture = new Texture2D(1, 1, TextureFormat.RFloat, true);
            RenderTexture.active = onePixelRenderTexture;
            onePixelTexture.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            var ssim = onePixelTexture.GetPixel(0, 0).r;

            return ssim;
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
            bool isNormalMap = refImporter.textureType == TextureImporterType.NormalMap;

            if (enabledMetrics[TextureQuality.Metric.PSNR_mip0])
            {
                float psnr = CalculatePSNR(refTexture, targetTexture, isNormalMap, refImporter.sRGBTexture, 0, false);        
                quality.SetResult(TextureQuality.Metric.PSNR_mip0, (psnr, "dB"));
            }

            if (enabledMetrics[TextureQuality.Metric.PSNR_mip1]) 
            {
                float psnr = CalculatePSNR(refTexture, targetTexture, isNormalMap, refImporter.sRGBTexture, 1, false);
                quality.SetResult(TextureQuality.Metric.PSNR_mip1, (psnr, "dB"));
            }

            if (enabledMetrics[TextureQuality.Metric.SSIM_mip0])
            {
                float ssim = CalculateSSIM(refTexture, targetTexture, isNormalMap, refImporter.sRGBTexture, 0, false);
                quality.SetResult(TextureQuality.Metric.SSIM_mip0, (ssim * 100, ""));
            }

            if (enabledMetrics[TextureQuality.Metric.SSIM_mip1])
            {
                float ssim = CalculateSSIM(refTexture, targetTexture, isNormalMap, refImporter.sRGBTexture, 1, false);
                quality.SetResult(TextureQuality.Metric.SSIM_mip1, (ssim * 100, ""));
            }

            //psnr = CalculatePSNR(refTexture, targetTexture, isNormalMap, refImporter.sRGBTexture, 0, true);
            //quality.SetResult(TextureQuality.Metric.PSNR_derivative, (psnr, "dB"));

            return quality;
        }

        private bool CreateVariant(TextureVariant variant)
        {
            if (variant.ToString() == variants[0].ToString())
            {
                return false;
            }
            var rawTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GetVariantPath(variants[0]));
            var variantTexture = new Texture2D(rawTexture.width / variant.sizeReductionFactor, rawTexture.height / variant.sizeReductionFactor, rawTexture.format, rawTexture.mipmapCount > 1);
            Color[] colors = rawTexture.GetPixels((int)Mathf.Log(variant.sizeReductionFactor, 2));
            if ((AssetImporter.GetAtPath(GetVariantPath(variants[0])) as TextureImporter)?.textureType == TextureImporterType.NormalMap)
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i].r *= colors[i].a;
                    colors[i].r = colors[i].r * 2 - 1;
                    colors[i].g = colors[i].g * 2 - 1;
                    colors[i].b = Mathf.Sqrt(1 - Mathf.Clamp01(colors[i].r * colors[i].r + colors[i].g * colors[i].g));
                    colors[i].r = colors[i].r * 0.5f + 0.5f;
                    colors[i].g = colors[i].g * 0.5f + 0.5f;
                    colors[i].a = 1;
                }
            }
            variantTexture.SetPixels(colors);
            variantTexture.Apply();
            var path = GetVariantPath(variant);
            byte[] data = rawTexture.format == TextureFormat.RGBAHalf
                ? ImageConversion.EncodeToEXR(variantTexture, Texture2D.EXRFlags.CompressZIP)
                : ImageConversion.EncodeToPNG(variantTexture);
            System.IO.File.WriteAllBytes(Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length) + path, data);
            AssetDatabase.ImportAsset(path);
            return true;
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

        private T MiniTextureField<T>(string label, T texture, bool allowSceneObjects, params GUILayoutOption[] options) where T : Texture
        {
            var rect = EditorGUILayout.GetControlRect(true, 20f, EditorStyles.layerMaskField);
            var rects = new object[] {rect, new Rect(), new Rect()};
            typeof(EditorGUI)
                .GetMethod("GetRectsForMiniThumbnailField", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(null, rects);
            var textureRect = (Rect) rects[1];
            var labelRect = (Rect) rects[0];
            labelRect.x = textureRect.width + 2;
            EditorGUI.LabelField(labelRect, label);
            return EditorGUI.ObjectField(textureRect, texture, typeof(T), false) as T;
        }

        public void OnGUI()
        {
            var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            assetRootPath = path.Substring(0, path.LastIndexOf('/'));
            assetRootPath = assetRootPath.Substring(0, assetRootPath.LastIndexOf('/'));
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (packageInfo?.source != UnityEditor.PackageManager.PackageSource.Embedded)
            {
                assetRootPath = "Assets/d4rkAvatarOptimizer";
                if (!AssetDatabase.IsValidFolder(assetRootPath))
                {
                    AssetDatabase.CreateFolder("Assets", "d4rkAvatarOptimizer");
                }
            }   
            if (GUILayout.Button($"Clear Cache ({AssetDatabase.GetSubFolders(assetRootPath + "/TextureAnalyzer").Length})"))
            {
                AssetDatabase.DeleteAsset(assetRootPath + "/TextureAnalyzer");
                quality = null;
            }
            if (!AssetDatabase.IsValidFolder(assetRootPath + "/TextureAnalyzer"))
            {
                AssetDatabase.CreateFolder(assetRootPath, "TextureAnalyzer");
            }
            assetRootPath += "/TextureAnalyzer";

            string texturePath = AssetDatabase.GetAssetPath(texture);
            string fileExtension = System.IO.Path.GetExtension(texturePath);
            string texGUID = AssetDatabase.AssetPathToGUID(texturePath);
            string texFolder = assetRootPath + "/" + texGUID;
            var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;

            bool hdr = texture != null && (texture.format == TextureFormat.RGBAHalf || texture.format == TextureFormat.BC6H);
            string textureInfo = texture == null ? "None" : $"{texture.name} | {texture.width}x{texture.height} | {texture.format}";
            if (textureImporter != null)
            {
                if (hdr)
                    textureInfo += " | HDR";
                if (textureImporter.sRGBTexture)
                    textureInfo += " | sRGB";
                if (textureImporter.mipmapEnabled)
                    textureInfo += " | Mips";
                if (textureImporter.textureType == TextureImporterType.NormalMap)
                    textureInfo += " | NormalMap";
                else if (textureImporter.DoesSourceTextureHaveAlpha())
                    textureInfo += " | Alpha";
            }
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Space();
            staticTexture = texture = MiniTextureField(textureInfo, texture, false);
            EditorGUILayout.Space();
            if (showMetricOptions = EditorGUILayout.Foldout(showMetricOptions, "Quality Metric Options"))
            {
                EditorGUI.indentLevel++;
                sortMetric = (TextureQuality.Metric) EditorGUILayout.EnumPopup("Sort Metric", sortMetric);
                enabledMetrics[sortMetric] = true;
                foreach (var metric in System.Enum.GetValues(typeof(TextureQuality.Metric)).Cast<TextureQuality.Metric>())
                {
                    if (metric == sortMetric)
                        GUI.enabled = false;
                    enabledMetrics[metric] = EditorGUILayout.Toggle(metric.ToString(), enabledMetrics[metric]);
                    GUI.enabled = true;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space();

            string disableButtonError = "";

            if (textureImporter == null)
            {
                disableButtonError = "No TextureImporter found";
            }
            if (texture == null)
            {
                disableButtonError = "No Texture selected";
            }
            else if (texture.mipmapCount == 1)
            {
                disableButtonError = "Texture has no mipmaps";
            }

            variants = variantsRGB;
            if (hdr)
            {
                variants = variantsHDR;
            }
            else if (textureImporter?.textureType == TextureImporterType.NormalMap)
            {
                variants = variantsNormal;
            }
            else if (textureImporter?.DoesSourceTextureHaveAlpha() ?? false)
            {
                variants = variantsRGBA;
            }

            GUI.enabled = disableButtonError == "";

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
                        if(!CreateVariant(variant))
                        {
                            AssetDatabase.CopyAsset(texturePath, variantPath);
                        }
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

            if (!GUI.enabled)
            {
                GUI.enabled = true;
                EditorGUILayout.HelpBox(disableButtonError, MessageType.Error);
                return;
            }

            if (EditorGUI.EndChangeCheck())
                quality = null;

            var sizes = variants.Select(v => GetVariantSize(v)).ToArray();
            var maxVram = sizes[0].vram;
            var maxAssetBundle = sizes[0].assetBundle;
            if (maxVram < 0 || maxAssetBundle < 0)
            {
                EditorGUILayout.HelpBox("Please click the \"Analyze Variants\" button first.", MessageType.Warning);
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
                .OrderByDescending(v => v.quality.GetResult(sortMetric)?.value ?? 0)
                .ToArray();

            EditorGUILayout.Space(5);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            {
                var variant = variantsWithQualityResult[0].variant;
                var quality = variantsWithQualityResult[0].quality;
                string variantPath = GetVariantPath(variant);
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(variantPath);
                if (asset == null)
                {
                    EditorGUILayout.HelpBox($"Variant {variant} is not analyzed.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField($"{variant}:");
                    EditorGUI.indentLevel++;
                    var size = GetVariantSize(variant);
                    EditorGUILayout.LabelField($"VRAM: {FormatByteSize(size.vram)} ({(size.vram / maxVram * 100):F2}%)");
                    EditorGUILayout.LabelField($"Download: {FormatByteSize(size.assetBundle)} ({(size.assetBundle / maxAssetBundle * 100):F2}%)");
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(5);
                }
            }

            foreach (var tuple in variantsWithQualityResult.Skip(1))
            {
                var variant = tuple.variant;
                var quality = tuple.quality;
                string variantPath = GetVariantPath(variant);
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(variantPath);
                if (asset == null)
                {
                    EditorGUILayout.HelpBox($"Variant {variant} is not analyzed.", MessageType.Warning);
                    continue;
                }
                EditorGUILayout.LabelField($"{variant}:");
                EditorGUI.indentLevel++;
                var size = GetVariantSize(variant);
                EditorGUILayout.LabelField($"VRAM: {FormatByteSize(size.vram)} ({(size.vram / maxVram * 100):F2}%)");
                EditorGUILayout.LabelField($"Download: {FormatByteSize(size.assetBundle)} ({(size.assetBundle / maxAssetBundle * 100):F2}%)");
                foreach(var entry in quality.GetResults())
                {
                    EditorGUILayout.LabelField($"{entry.metric}: {entry.result.value:F2}{entry.result.unit}");
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif