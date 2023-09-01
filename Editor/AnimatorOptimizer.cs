#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using BlendableLayer = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer;

namespace d4rkpl4y3r.AvatarOptimizer
{
    // based on https://github.com/VRLabs/Avatars-3.0-Manager/blob/main/Editor/AnimatorCloner.cs
    public class AnimatorOptimizer
    {
        private string assetPath;
        private AnimatorController source;
        private AnimatorController target;
        private Dictionary<AnimatorState, AnimatorState> stateMap = new Dictionary<AnimatorState, AnimatorState>();
        private Dictionary<AnimatorStateMachine, AnimatorStateMachine> stateMachineMap = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
        private Dictionary<int, int> fxLayerMap = new Dictionary<int, int>();
        private HashSet<int> layersToMerge = new HashSet<int>();
        private HashSet<int> layersToDestroy = new HashSet<int>();
        private HashSet<string> parametersToChangeToFloat = new HashSet<string>();

        private AnimatorOptimizer(AnimatorController target, AnimatorController source)
        {
            this.target = target;
            this.source = source;
            assetPath = AssetDatabase.GetAssetPath(target);
        }

        public static AnimatorController Copy(AnimatorController source, string path, Dictionary<int, int> fxLayerMap)
        {
            if (AssetDatabase.IsSubAsset(source))
            {
                return Run(source, path, fxLayerMap, new List<int>(), new List<int>());
            }
            // I try to use CopyAsset for non FX layers as the other way broke falling animations with gogo loco
            // however I can't use it if the source is a sub asset
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path);
            var target = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            target.name = $"{source.name}(Optimized)";
            var optimizer = new AnimatorOptimizer(target, source);
            optimizer.fxLayerMap = new Dictionary<int, int>(fxLayerMap);
            optimizer.FixAllLayerControlBehaviours();
            return target;
        }

        public static AnimatorController Run(AnimatorController source, string path, Dictionary<int, int> fxLayerMap, List<int> layersToMerge, List<int> layersToDestroy)
        {
            var target = new AnimatorController();
            target.name = $"{source.name}(Optimized)";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BinarySerializationSO>(), path);
            AssetDatabase.AddObjectToAsset(target, path);
            var optimizer = new AnimatorOptimizer(target, source);
            optimizer.layersToMerge = new HashSet<int>(layersToMerge);
            optimizer.layersToDestroy = new HashSet<int>(layersToDestroy);
            optimizer.fxLayerMap = new Dictionary<int, int>(fxLayerMap);
            return optimizer.Run();
        }

        private AnimatorController Run()
        {
            for (int i = 0; i < source.layers.Length; i++)
            {
                if (layersToMerge.Contains(i) && source.layers[i].stateMachine.states.Length == 2)
                {
                    parametersToChangeToFloat.UnionWith(source.layers[i].stateMachine.states.SelectMany(x => x.state.transitions).SelectMany(x => x.conditions).Select(x => x.parameter));
                }
            }

            foreach (var p in source.parameters)
            {
                bool changeToFloat = parametersToChangeToFloat.Contains(p.name);
                var newP = new AnimatorControllerParameter
                {
                    name = p.name,
                    type = changeToFloat ? AnimatorControllerParameterType.Float : p.type,
                    defaultBool = p.defaultBool,
                    defaultFloat = changeToFloat ? (p.defaultBool ? 1f : 0f) : p.defaultFloat,
                    defaultInt = p.defaultInt
                };
                if (target.parameters.Count(x => x.name.Equals(newP.name)) == 0)
                {
                    target.AddParameter(newP);
                }
            }

            if (layersToMerge.Count > 0)
            {
                var blendTreeDummyWeight = new AnimatorControllerParameter
                {
                    name = "d4rkAvatarOptimizer_MergedLayers_Weight",
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 1f,
                    defaultBool = true,
                    defaultInt = 1
                };
                target.AddParameter(blendTreeDummyWeight);
            }

            for (int i = 0; i < source.layers.Length; i++)
            {
                if (layersToMerge.Contains(i) || layersToDestroy.Contains(i))
                {
                    continue;
                }
                AnimatorControllerLayer newL = CloneLayer(source.layers[i], i == 0);
                newL.name = target.MakeUniqueLayerName(newL.name);
                newL.stateMachine.name = newL.name;
                target.AddLayer(newL);
            }

            MergeLayers();

            EditorUtility.SetDirty(target);

            return target;
        }

        private void FixAllLayerControlBehaviours()
        {
            for (int i = 0; i < target.layers.Length; i++)
            {
                FixLayerControlBehavioursInStateMachine(target.layers[i].stateMachine);
            }
            EditorUtility.SetDirty(target);
        }

        private void FixLayerControlBehavioursInStateMachine(AnimatorStateMachine stateMachine)
        {
            var behaviours = stateMachine.behaviours;
            for (int i = 0; i < behaviours.Length; i++)
            {
                FixLayerControlBehaviour(behaviours[i]);
            }
            var stateMachines = stateMachine.stateMachines;
            for (int i = 0; i < stateMachines.Length; i++)
            {
                FixLayerControlBehavioursInStateMachine(stateMachines[i].stateMachine);
            }
            var states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                behaviours = states[i].state.behaviours;
                for (int j = 0; j < behaviours.Length; j++)
                {
                    FixLayerControlBehaviour(behaviours[j]);
                }
            }
        }

        private void FixLayerControlBehaviour(StateMachineBehaviour behaviour)
        {
            if (behaviour is VRC_AnimatorLayerControl layerControl)
            {
                if (layerControl.playable == BlendableLayer.FX && fxLayerMap.TryGetValue(layerControl.layer, out int newLayer))
                {
                    layerControl.layer = newLayer;
                    EditorUtility.SetDirty(layerControl);
                }
            }
        }

        private AnimationClip CloneAndFlipCurves(AnimationClip clip)
        {
            var newClip = GameObject.Instantiate(clip);
            newClip.name = $"{clip.name}(Flipped)";
            newClip.ClearCurves();
            newClip.hideFlags = HideFlags.HideInHierarchy;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                curve = new AnimationCurve(curve.keys.Select(x => new Keyframe(x.time, 1 - x.value, -x.inTangent, -x.outTangent)).ToArray());
                AnimationUtility.SetEditorCurve(newClip, binding, curve);
            }
            AssetDatabase.AddObjectToAsset(newClip, assetPath);
            return newClip;
        }

        private AnimationClip CloneFromTime(AnimationClip clip, float time, string name = null)
        {
            var newClip = GameObject.Instantiate(clip);
            newClip.name = name ?? $"{clip.name}(From {time})";
            newClip.ClearCurves();
            newClip.hideFlags = HideFlags.HideInHierarchy;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(newClip, binding, new AnimationCurve(new Keyframe[] { new Keyframe(0, curve.Evaluate(time)) }));
            }
            AssetDatabase.AddObjectToAsset(newClip, assetPath);
            return newClip;
        }

        private void MergeLayers()
        {
            if (layersToMerge.Count == 0)
            {
                return;
            }
            var directBlendTree = new BlendTree()
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = "MergedToggles",
                blendType = BlendTreeType.Direct
            };
            var motions = new List<ChildMotion>();
            int currentHeight = 0;
            foreach (var i in layersToMerge)
            {
                var layer = source.layers[i].stateMachine;
                string param = null;
                var treeMotions = new List<Motion>();
                if (layer.states.Length == 2)
                {
                    if (layer.states.All(s => s.state.transitions.Length == 1))
                    {
                        param = layer.states[0].state.transitions[0].conditions[0].parameter;
                        int onStateIndex = layer.states[0].state.transitions[0].conditions[0].mode == AnimatorConditionMode.If ? 1 : 0;
                        var onClip = layer.states[onStateIndex].state.motion as AnimationClip;
                        var offClip = layer.states[1 - onStateIndex].state.motion as AnimationClip;
                        if (onClip == null)
                            onClip = CloneAndFlipCurves(offClip);
                        if (offClip == null)
                            offClip = CloneAndFlipCurves(onClip);
                        treeMotions.Add(offClip);
                        treeMotions.Add(onClip);
                    }
                    else
                    {
                        int singleIndex = layer.states[0].state.transitions.Length == 1 ? 0 : 1;
                        var singleClip = layer.states[singleIndex].state.motion as AnimationClip;
                        var doubleClip = layer.states[1 - singleIndex].state.motion as AnimationClip;
                        if (singleClip == null)
                            singleClip = CloneAndFlipCurves(doubleClip);
                        if (doubleClip == null)
                            doubleClip = CloneAndFlipCurves(singleClip);
                        var conditions = layer.states[singleIndex].state.transitions[0].conditions;
                        param = conditions[0].parameter;
                        var innerTreeMotions = new ChildMotion[2] {
                            new ChildMotion() { motion = singleClip, timeScale = 1f },
                            new ChildMotion() { motion = doubleClip, timeScale = 1f }
                        };
                        if (conditions[1].mode == AnimatorConditionMode.IfNot)
                        {
                            innerTreeMotions = innerTreeMotions.Reverse().ToArray();
                        }
                        var tree = new BlendTree()
                        {
                            hideFlags = HideFlags.HideInHierarchy,
                            name = "InnerFusedBoolCondition",
                            blendType = BlendTreeType.Simple1D,
                            useAutomaticThresholds = true,
                            maxThreshold = 1f,
                            minThreshold = 0f,
                            blendParameter = conditions[1].parameter,
                            children = innerTreeMotions,
                        };
                        AssetDatabase.AddObjectToAsset(tree, assetPath);
                        treeMotions.Add(singleClip);
                        treeMotions.Add(tree);
                        if (conditions[0].mode == AnimatorConditionMode.IfNot)
                        {
                            treeMotions.Reverse();
                        }
                    }
                }
                else
                {
                    if (layer.states[0].state.motion is BlendTree blendTreeToClone)
                    {
                        param = "d4rkAvatarOptimizer_MergedLayers_Weight";
                        var clonedBlendTree = CloneBlendTree(null, blendTreeToClone);
                        clonedBlendTree.name = $"{clonedBlendTree.name} (cloned)";
                        clonedBlendTree.hideFlags = HideFlags.HideInHierarchy;
                        AssetDatabase.AddObjectToAsset(clonedBlendTree, assetPath);
                        treeMotions.Add(clonedBlendTree);
                    }
                    else
                    {
                        param = layer.states[0].state.timeParameter;
                        float maxKeyframeTime = 0;
                        var clip = layer.states[0].state.motion as AnimationClip;
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        {
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            maxKeyframeTime = Mathf.Max(maxKeyframeTime, curve.keys.Max(x => x.time));
                        }
                        treeMotions.Add(CloneFromTime(clip, 0, $"{clip.name} (0%)"));
                        treeMotions.Add(CloneFromTime(clip, 0.25f * maxKeyframeTime, $"{clip.name} (25%)"));
                        treeMotions.Add(CloneFromTime(clip, 0.5f * maxKeyframeTime, $"{clip.name} (50%)"));
                        treeMotions.Add(CloneFromTime(clip, 0.75f * maxKeyframeTime, $"{clip.name} (75%)"));
                        treeMotions.Add(CloneFromTime(clip, maxKeyframeTime, $"{clip.name} (100%)"));
                    }
                }
                var layerTree = new BlendTree()
                {
                    hideFlags = HideFlags.HideInHierarchy,
                    name = source.layers[i].name,
                    blendType = BlendTreeType.Simple1D,
                    useAutomaticThresholds = true,
                    maxThreshold = 1f,
                    minThreshold = 0f,
                    blendParameter = param,
                    children = treeMotions.Select(x => new ChildMotion() { motion = x, timeScale = 1f }).ToArray()
                };
                AssetDatabase.AddObjectToAsset(layerTree, assetPath);
                motions.Add(new ChildMotion()
                {
                    motion = layerTree,
                    directBlendParameter = "d4rkAvatarOptimizer_MergedLayers_Weight",
                    timeScale = 1f
                });
                currentHeight++;
            }
            directBlendTree.children = motions.ToArray();
            AssetDatabase.AddObjectToAsset(directBlendTree, assetPath);
            var state = new AnimatorState()
            {
                name = "d4rkAvatarOptimizer_MergedLayers",
                writeDefaultValues = true,
                hideFlags = HideFlags.HideInHierarchy,
                motion = directBlendTree
            };
            AssetDatabase.AddObjectToAsset(state, assetPath);
            var stateMachine = new AnimatorStateMachine()
            {
                name = "d4rkAvatarOptimizer_MergedLayers",
                hideFlags = HideFlags.HideInHierarchy,
                states = new ChildAnimatorState[1] {
                    new ChildAnimatorState()
                    {
                        state = state,
                        position = new Vector3(250, 0, 0)
                    }
                }
            };
            AssetDatabase.AddObjectToAsset(stateMachine, assetPath);
            target.AddLayer(new AnimatorControllerLayer
            {
                avatarMask = null,
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 1f,
                iKPass = false,
                name = target.MakeUniqueLayerName("d4rkAvatarOptimizer_MergedLayers"),
                syncedLayerAffectsTiming = false,
                stateMachine = stateMachine
            });
        }

        private AnimatorControllerLayer CloneLayer(AnimatorControllerLayer old, bool isFirstLayer = false)
        {
            var n = new AnimatorControllerLayer
            {
                avatarMask = old.avatarMask,
                blendingMode = old.blendingMode,
                defaultWeight = isFirstLayer ? 1f : old.defaultWeight,
                iKPass = old.iKPass,
                name = old.name,
                syncedLayerAffectsTiming = old.syncedLayerAffectsTiming,
                stateMachine = CloneStateMachine(old.stateMachine)
            };
            CloneTransitions(old.stateMachine, n.stateMachine);
            return n;
        }

        private AnimatorStateMachine CloneStateMachine(AnimatorStateMachine old)
        {
            var n = new AnimatorStateMachine
            {
                anyStatePosition = old.anyStatePosition,
                entryPosition = old.entryPosition,
                exitPosition = old.exitPosition,
                hideFlags = old.hideFlags,
                name = old.name,
                parentStateMachinePosition = old.parentStateMachinePosition,
                stateMachines = old.stateMachines.Select(x => CloneChildStateMachine(x)).ToArray(),
                states = old.states.Select(x => CloneChildAnimatorState(x)).ToArray()
            };
            stateMachineMap[old] = n;
            
            AssetDatabase.AddObjectToAsset(n, assetPath);
            n.defaultState = FindMatchingState(old.defaultState);

            foreach (var oldb in old.behaviours)
            {
                var behaviour = n.AddStateMachineBehaviour(oldb.GetType());
                CloneBehaviourParameters(oldb, behaviour);
            }
            return n;
        }

        private ChildAnimatorStateMachine CloneChildStateMachine(ChildAnimatorStateMachine old)
        {
            var n = new ChildAnimatorStateMachine
            {
                position = old.position,
                stateMachine = CloneStateMachine(old.stateMachine)
            };
            return n;
        }

        private ChildAnimatorState CloneChildAnimatorState(ChildAnimatorState old)
        {
            var n = new ChildAnimatorState
            {
                position = old.position,
                state = CloneAnimatorState(old.state)
            };
            foreach (var oldb in old.state.behaviours)
            {
                var behaviour = n.state.AddStateMachineBehaviour(oldb.GetType());
                CloneBehaviourParameters(oldb, behaviour);
            }
            return n;
        }

        private AnimatorState CloneAnimatorState(AnimatorState old)
        {
            // Checks if the motion is a blend tree, to avoid accidental blend tree sharing between animator assets
            Motion motion = old.motion;
            if (motion is BlendTree oldTree)
            {
                var tree = CloneBlendTree(null, oldTree);
                motion = tree;
                // need to save the blend tree into the animator
                tree.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(motion, assetPath);
            }

            var n = new AnimatorState
            {
                cycleOffset = old.cycleOffset,
                cycleOffsetParameter = old.cycleOffsetParameter,
                cycleOffsetParameterActive = old.cycleOffsetParameterActive,
                hideFlags = old.hideFlags,
                iKOnFeet = old.iKOnFeet,
                mirror = old.mirror,
                mirrorParameter = old.mirrorParameter,
                mirrorParameterActive = old.mirrorParameterActive,
                motion = motion,
                name = old.name,
                speed = old.speed,
                speedParameter = old.speedParameter,
                speedParameterActive = old.speedParameterActive,
                tag = old.tag,
                timeParameter = old.timeParameter,
                timeParameterActive = old.timeParameterActive,
                writeDefaultValues = old.writeDefaultValues
            };
            stateMap[old] = n;
            AssetDatabase.AddObjectToAsset(n, assetPath);
            return n;
        }

        // Taken from here: https://gist.github.com/phosphoer/93ca8dcbf925fc006e4e9f6b799c13b0
        private BlendTree CloneBlendTree(BlendTree parentTree, BlendTree oldTree)
        {
            // Create a child tree in the destination parent, this seems to be the only way to correctly 
            // add a child tree as opposed to AddChild(motion)
            BlendTree pastedTree = new BlendTree();
            pastedTree.name = oldTree.name;
            pastedTree.blendType = oldTree.blendType;
            pastedTree.blendParameter = oldTree.blendParameter;
            pastedTree.blendParameterY = oldTree.blendParameterY;
            pastedTree.minThreshold = oldTree.minThreshold;
            pastedTree.maxThreshold = oldTree.maxThreshold;
            pastedTree.useAutomaticThresholds = oldTree.useAutomaticThresholds;

            // Recursively duplicate the tree structure
            // Motions can be directly added as references while trees must be recursively to avoid accidental sharing
            var source = oldTree.children;
            var children = new ChildMotion[source.Length];
            for(int i = 0; i < children.Length; i++)
            {
                var child = source[i];

                var childMotion = new ChildMotion
                {
                    timeScale = child.timeScale,
                    position = child.position,
                    cycleOffset = child.cycleOffset,
                    mirror = child.mirror,
                    threshold = child.threshold,
                    directBlendParameter = child.directBlendParameter
                };

                if (child.motion is BlendTree tree)
                {
                    var childTree = CloneBlendTree(pastedTree, tree);
                    childMotion.motion = childTree;
                    // need to save the blend tree into the animator
                    childTree.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(childTree, assetPath);
                }
                else
                {
                    childMotion.motion = child.motion;
                }
                
                children[i] = childMotion;
            }
            pastedTree.children = children;

            return pastedTree;
        }

        private void CloneBehaviourParameters(StateMachineBehaviour old, StateMachineBehaviour n)
        {
            if (old.GetType() != n.GetType())
            {
                throw new System.ArgumentException("2 state machine behaviours that should be of the same type are not.");
            }
            switch (n)
            {
                case VRCAnimatorLayerControl l:
                    {
                        var o = old as VRCAnimatorLayerControl;
                        l.ApplySettings = o.ApplySettings;
                        l.blendDuration = o.blendDuration;
                        l.debugString = o.debugString;
                        l.goalWeight = o.goalWeight;
                        l.layer = o.layer;
                        l.playable = o.playable;
                        if (l.playable == BlendableLayer.FX && fxLayerMap.TryGetValue(o.layer, out int newLayer))
                        {
                            l.layer = newLayer;
                        }
                        break;
                    }
                case VRCAnimatorLocomotionControl l:
                    {
                        var o = old as VRCAnimatorLocomotionControl;
                        l.ApplySettings = o.ApplySettings;
                        l.debugString = o.debugString;
                        l.disableLocomotion = o.disableLocomotion;
                        break;
                    }
                case VRCAnimatorTemporaryPoseSpace l:
                    {
                        var o = old as VRCAnimatorTemporaryPoseSpace;
                        l.ApplySettings = o.ApplySettings;
                        l.debugString = o.debugString;
                        l.delayTime = o.delayTime;
                        l.enterPoseSpace = o.enterPoseSpace;
                        l.fixedDelay = o.fixedDelay;
                        break;
                    }
                case VRCAnimatorTrackingControl l:
                    {
                        var o = old as VRCAnimatorTrackingControl;
                        l.ApplySettings = o.ApplySettings;
                        l.debugString = o.debugString;
                        l.trackingEyes = o.trackingEyes;
                        l.trackingHead = o.trackingHead;
                        l.trackingHip = o.trackingHip;
                        l.trackingLeftFingers = o.trackingLeftFingers;
                        l.trackingLeftFoot = o.trackingLeftFoot;
                        l.trackingLeftHand = o.trackingLeftHand;
                        l.trackingMouth = o.trackingMouth;
                        l.trackingRightFingers = o.trackingRightFingers;
                        l.trackingRightFoot = o.trackingRightFoot;
                        l.trackingRightHand = o.trackingRightHand;
                        break;
                    }
                case VRCAvatarParameterDriver l:
                    {
                        var d = old as VRCAvatarParameterDriver;
                        l.debugString = d.debugString;
                        l.localOnly = d.localOnly;
                        l.isLocalPlayer = d.isLocalPlayer;
                        l.initialized = d.initialized;
                        l.parameters = d.parameters.ConvertAll(p =>
                        {
                            return new VRC_AvatarParameterDriver.Parameter 
                            { 
                                name = p.name, 
                                value = p.value, 
                                chance = p.chance, 
                                valueMin = p.valueMin, 
                                valueMax = p.valueMax, 
                                type = p.type, 
                                source = p.source, 
                                convertRange = p.convertRange, 
                                destMax = p.destMax, 
                                destMin = p.destMin, 
                                destParam = p.destParam, 
                                sourceMax = p.sourceMax, 
                                sourceMin = p.sourceMin, 
                                sourceParam = p.sourceParam
                            };
                        });
                        break;
                    }
                case VRCPlayableLayerControl l:
                    {
                        var o = old as VRCPlayableLayerControl;
                        l.ApplySettings = o.ApplySettings;
                        l.blendDuration = o.blendDuration;
                        l.debugString = o.debugString;
                        l.goalWeight = o.goalWeight;
                        l.layer = o.layer;
                        l.outputParamHash = o.outputParamHash;
                        break;
                    }
            }
        }

        private List<AnimatorState> GetStatesRecursive(AnimatorStateMachine sm)
        {
            List<AnimatorState> childrenStates = sm.states.Select(x => x.state).ToList();
            foreach (var child in sm.stateMachines.Select(x => x.stateMachine))
                childrenStates.AddRange(GetStatesRecursive(child));

            return childrenStates;
        }

        private List<AnimatorStateMachine> GetStateMachinesRecursive(AnimatorStateMachine sm,
            IDictionary<AnimatorStateMachine, AnimatorStateMachine> newAnimatorsByChildren = null)
        {
            List<AnimatorStateMachine> childrenSm = sm.stateMachines.Select(x => x.stateMachine).ToList();

            List<AnimatorStateMachine> gcsm = new List<AnimatorStateMachine>();
            gcsm.Add(sm);
            foreach (var child in childrenSm)
            {
                newAnimatorsByChildren?.Add(child, sm);
                gcsm.AddRange(GetStateMachinesRecursive(child, newAnimatorsByChildren));
            }
            
            return gcsm;
        }

        private AnimatorState FindMatchingState(AnimatorState original)
        {
            return original == null ? null : stateMap.TryGetValue(original, out AnimatorState state) ? state : null;
        }
        
        private AnimatorStateMachine FindMatchingStateMachine(AnimatorStateMachine original)
        {
            return original == null ? null : stateMachineMap.TryGetValue(original, out AnimatorStateMachine stateMachine) ? stateMachine : null;
        }

        private void CloneTransitions(AnimatorStateMachine old, AnimatorStateMachine n)
        {
            List<AnimatorState> oldStates = GetStatesRecursive(old);
            List<AnimatorState> newStates = GetStatesRecursive(n);
            var newAnimatorsByChildren = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
            var oldAnimatorsByChildren = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
            List<AnimatorStateMachine> oldStateMachines = GetStateMachinesRecursive(old, oldAnimatorsByChildren);
            List<AnimatorStateMachine> newStateMachines = GetStateMachinesRecursive(n, newAnimatorsByChildren);
            // Generate state transitions
            for (int i = 0; i < oldStates.Count; i++)
            {
                foreach (var transition in oldStates[i].transitions)
                {
                    AnimatorStateTransition newTransition = null;
                    if (transition.isExit && transition.destinationState == null && transition.destinationStateMachine == null)
                    {
                        newTransition = newStates[i].AddExitTransition();
                    }
                    else if (transition.destinationState != null)
                    {
                        var dstState = FindMatchingState(transition.destinationState);
                        if (dstState != null)
                            newTransition = newStates[i].AddTransition(dstState);
                    }
                    else if (transition.destinationStateMachine != null)
                    {
                        var dstState = FindMatchingStateMachine(transition.destinationStateMachine);
                        if (dstState != null)
                            newTransition = newStates[i].AddTransition(dstState);
                    }

                    if (newTransition != null)
                        ApplyTransitionSettings(transition, newTransition);
                }
            }
            
            for (int i = 0; i < oldStateMachines.Count; i++)
            {
                if(oldAnimatorsByChildren.ContainsKey(oldStateMachines[i]) && newAnimatorsByChildren.ContainsKey(newStateMachines[i]))
                {
                    foreach (var transition in oldAnimatorsByChildren[oldStateMachines[i]].GetStateMachineTransitions(oldStateMachines[i]))
                    {
                        AnimatorTransition newTransition = null;
                        if (transition.isExit && transition.destinationState == null && transition.destinationStateMachine == null)
                        {
                            newTransition = newAnimatorsByChildren[newStateMachines[i]].AddStateMachineExitTransition(newStateMachines[i]);
                        }
                        else if (transition.destinationState != null)
                        {
                            var dstState = FindMatchingState(transition.destinationState);
                            if (dstState != null)
                                newTransition = newAnimatorsByChildren[newStateMachines[i]].AddStateMachineTransition(newStateMachines[i], dstState);
                        }
                        else if (transition.destinationStateMachine != null)
                        {
                            var dstState = FindMatchingStateMachine(transition.destinationStateMachine);
                            if (dstState != null)
                                newTransition = newAnimatorsByChildren[newStateMachines[i]].AddStateMachineTransition(newStateMachines[i], dstState);
                        }

                        if (newTransition != null)
                            ApplyTransitionSettings(transition, newTransition);
                    }
                }
                // Generate AnyState transitions
                GenerateStateMachineBaseTransitions(oldStateMachines[i], newStateMachines[i], oldStates, newStates, oldStateMachines, newStateMachines);
            }
        }

        private void GenerateStateMachineBaseTransitions(AnimatorStateMachine old, AnimatorStateMachine n, List<AnimatorState> oldStates,
            List<AnimatorState> newStates, List<AnimatorStateMachine> oldStateMachines, List<AnimatorStateMachine> newStateMachines)
        {
            foreach (var transition in old.anyStateTransitions)
            {
                AnimatorStateTransition newTransition = null;
                if (transition.destinationState != null)
                {
                    var dstState = FindMatchingState(transition.destinationState);
                    if (dstState != null)
                        newTransition = n.AddAnyStateTransition(dstState);
                }
                else if (transition.destinationStateMachine != null)
                {
                    var dstState = FindMatchingStateMachine(transition.destinationStateMachine);
                    if (dstState != null)
                        newTransition = n.AddAnyStateTransition(dstState);
                }

                if (newTransition != null)
                    ApplyTransitionSettings(transition, newTransition);
            }

            // Generate EntryState transitions
            foreach (var transition in old.entryTransitions)
            {
                AnimatorTransition newTransition = null;
                if (transition.destinationState != null)
                {
                    var dstState = FindMatchingState(transition.destinationState);
                    if (dstState != null)
                        newTransition = n.AddEntryTransition(dstState);
                }
                else if (transition.destinationStateMachine != null)
                {
                    var dstState = FindMatchingStateMachine(transition.destinationStateMachine);
                    if (dstState != null)
                        newTransition = n.AddEntryTransition(dstState);
                }

                if (newTransition != null)
                    ApplyTransitionSettings(transition, newTransition);
            }
        }

        private void AddCondition(AnimatorTransitionBase transition, AnimatorCondition condition)
        {
            if (parametersToChangeToFloat.Contains(condition.parameter))
            {
                var mode = condition.mode == AnimatorConditionMode.If ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less;
                transition.AddCondition(mode, 0.5f, condition.parameter);
            }
            else
            {
                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }
        }

        private void ApplyTransitionSettings(AnimatorStateTransition transition, AnimatorStateTransition newTransition)
        {
            newTransition.canTransitionToSelf = transition.canTransitionToSelf;
            newTransition.duration = transition.duration;
            newTransition.exitTime = transition.exitTime;
            newTransition.hasExitTime = transition.hasExitTime;
            newTransition.hasFixedDuration = transition.hasFixedDuration;
            newTransition.hideFlags = transition.hideFlags;
            newTransition.isExit = transition.isExit;
            newTransition.mute = transition.mute;
            newTransition.name = transition.name;
            newTransition.offset = transition.offset;
            newTransition.interruptionSource = transition.interruptionSource;
            newTransition.orderedInterruption = transition.orderedInterruption;
            newTransition.solo = transition.solo;
            foreach (var condition in transition.conditions)
                AddCondition(newTransition, condition);
            
        }

        private void ApplyTransitionSettings(AnimatorTransition transition, AnimatorTransition newTransition)
        {
            newTransition.hideFlags = transition.hideFlags;
            newTransition.isExit = transition.isExit;
            newTransition.mute = transition.mute;
            newTransition.name = transition.name;
            newTransition.solo = transition.solo;
            foreach (var condition in transition.conditions)
                AddCondition(newTransition, condition);
        }
    }
}
#endif