#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Dynamics.Contact.Components;

namespace d4rkpl4y3r.AvatarOptimizer.Util
{
    public class MaterialContactWrapper
    {
        public readonly List<VRCContactReceiver> receivers = new();
        public Transform transform => receivers[0].transform;
        public List<Renderer> targetRenderers = new();
        private MaterialContactWrapper(VRCContactReceiver receiver)
        {
            receivers.Add(receiver);
            targetRenderers.AddRange(receiver.shaderTargetRenderers.Where(r => r != null));
        }

        public void ApplyChanges()
        {
            targetRenderers.RemoveAll(r => r == null);
            int maxRenderers = VRCContactReceiver.SHADER_MAX_UPDATED_RENDERERS;
            int needed = Mathf.CeilToInt(targetRenderers.Count / (float)maxRenderers);
            for (int i = needed; i < receivers.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(receivers[i]);
            }
            if (needed < receivers.Count)
            {
                receivers.RemoveRange(needed, receivers.Count - needed);
            }
            for (int i = 0; i < needed; i++)
            {
                int start = i * maxRenderers;
                int count = Mathf.Min(maxRenderers, targetRenderers.Count - start);
                receivers[i].shaderTargetRenderers = targetRenderers.GetRange(start, count).ToArray();
            }
        }

        public static List<MaterialContactWrapper> RawWrappers(IEnumerable<VRCContactReceiver> receivers)
        {
            return receivers.Where(IsMaterialContact).Select(c => new MaterialContactWrapper(c)).ToList();
        }

        public static List<MaterialContactWrapper> MergedWrappers(IEnumerable<VRCContactReceiver> receivers)
        {
            var grouped = receivers.Where(IsMaterialContact).GroupBy(r => r.transform).ToList();
            var wrappers = new List<MaterialContactWrapper>();
            foreach (var group in grouped)
            {
                var groupWrappers = new List<MaterialContactWrapper>();
                foreach (var receiver in group)
                {
                    var match = groupWrappers.FirstOrDefault(w => MatchSettings(w.receivers[0], receiver));
                    if (match != null)
                    {
                        match.receivers.Add(receiver);
                        match.targetRenderers.AddRange(receiver.shaderTargetRenderers.Where(r => r != null));
                    }
                    else
                    {
                        groupWrappers.Add(new MaterialContactWrapper(receiver));
                    }
                }
                wrappers.AddRange(groupWrappers);
            }
            return wrappers;
        }

        private static readonly string[] s_settingFieldNames =
        {
            // Receiver behavior
            nameof(VRCContactReceiver.allowSelf),
            nameof(VRCContactReceiver.allowOthers),
            nameof(VRCContactReceiver.useFaceProximity),
            nameof(VRCContactReceiver.receiverType),
            nameof(VRCContactReceiver.parameter),
            nameof(VRCContactReceiver.minVelocity),
            // Material contact specifics
            nameof(VRCContactReceiver.writeToMaterialsContactTags),
            nameof(VRCContactReceiver.separateShaderParameter),
            nameof(VRCContactReceiver.advancedParameters),
            // ContactBase dynamics settings
            nameof(VRCContactReceiver.contentTypes),
            nameof(VRCContactReceiver.localOnly),
            nameof(VRCContactReceiver.collisionTags),
            nameof(VRCContactReceiver.shapeType),
            nameof(VRCContactReceiver.radius),
            nameof(VRCContactReceiver.size),
            nameof(VRCContactReceiver.height),
            nameof(VRCContactReceiver.position),
            nameof(VRCContactReceiver.rotation),
        };

        private static readonly FieldInfo[] _receiverSettingFields = typeof(VRCContactReceiver)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => s_settingFieldNames.Contains(f.Name))
            .ToArray();

        private static bool MatchSettings(VRCContactReceiver a, VRCContactReceiver b)
        {
            if (a.GetRootTransform() != b.GetRootTransform())
                return false;
            foreach (var field in _receiverSettingFields)
            {
                var valueA = field.GetValue(a);
                var valueB = field.GetValue(b);
                if (!ValueEquals(valueA, valueB))
                    return false;
            }
            return true;
        }

        private static bool ValueEquals(object a, object b)
        {
            if (a is System.Collections.IEnumerable enumerableA && b is System.Collections.IEnumerable enumerableB)
                return Enumerable.SequenceEqual(enumerableA.Cast<object>(), enumerableB.Cast<object>());
            return Equals(a, b);
        }

        public static bool IsMaterialContact(VRCContactReceiver receiver)
        {
            return receiver != null &&
                receiver.writeToMaterials &&
                receiver.shaderTargetRenderers != null;
        }
    }
}
#endif