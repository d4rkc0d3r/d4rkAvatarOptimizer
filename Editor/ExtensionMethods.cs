#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor.Animations;
using UnityEngine;

namespace d4rkpl4y3r.AvatarOptimizer.Extensions
{
    public static class RendererExtensions
    {
        public static Mesh GetSharedMesh(this Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer)
            {
                return (renderer as SkinnedMeshRenderer).sharedMesh;
            }
            else if (renderer.TryGetComponent<MeshFilter>(out var filter))
            {
                return filter.sharedMesh;
            }
            else
            {
                return null;
            }
        }

        // using mesh.boneWeights = boneWeights; causes the mesh to always use 4 bone weights per vertex
        // this method allows to also use 2 or 1 bone weights per vertex if none of the vertices need more
        public static void SetBoneWeights(this Mesh mesh, BoneWeight[] boneWeights)
        {
            var weightsPerVertex = new byte[boneWeights.Length];
            var boneWeights1 = new List<BoneWeight1>();
            for (int i = 0; i < boneWeights.Length; i++)
            {
                weightsPerVertex[i] = 0;
                var w = boneWeights[i];
                if (w.weight0 > 0)
                {
                    weightsPerVertex[i]++;
                    boneWeights1.Add(new BoneWeight1() { boneIndex = w.boneIndex0, weight = w.weight0 });
                }
                if (w.weight1 > 0)
                {
                    weightsPerVertex[i]++;
                    boneWeights1.Add(new BoneWeight1() { boneIndex = w.boneIndex1, weight = w.weight1 });
                }
                if (w.weight2 > 0)
                {
                    weightsPerVertex[i]++;
                    boneWeights1.Add(new BoneWeight1() { boneIndex = w.boneIndex2, weight = w.weight2 });
                }
                if (w.weight3 > 0)
                {
                    weightsPerVertex[i]++;
                    boneWeights1.Add(new BoneWeight1() { boneIndex = w.boneIndex3, weight = w.weight3 });
                }
            }
            var boneWeights1Array = boneWeights1.ToArray();
            var nativeBoneWeights1Array = new NativeArray<BoneWeight1>(boneWeights1Array, Allocator.Temp);
            var nativeWeightsPerVertex = new NativeArray<byte>(weightsPerVertex, Allocator.Temp);
            mesh.SetBoneWeights(nativeWeightsPerVertex, nativeBoneWeights1Array);
        }
    }

    public static class TransformExtensions
    {
        public static IEnumerable<Transform> GetAllDescendants(this Transform transform)
        {
            var stack = new Stack<Transform>();
            foreach (Transform child in transform)
            {
                stack.Push(child);
            }
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                foreach (Transform child in current)
                {
                    stack.Push(child);
                }
            }
        }
    }

    public static class AnimatorControllerExtensions
    {

        public static IEnumerable<AnimatorState> EnumerateAllStates(this AnimatorController controller)
        {
            var queue = new Queue<AnimatorStateMachine>();
            foreach (var layer in controller.layers)
            {
                queue.Enqueue(layer.stateMachine);
                while (queue.Count > 0)
                {
                    var stateMachine = queue.Dequeue();
                    foreach (var subStateMachine in stateMachine.stateMachines)
                    {
                        queue.Enqueue(subStateMachine.stateMachine);
                    }
                    foreach (var state in stateMachine.states.Select(s => s.state))
                    {
                        yield return state;
                    }
                }
            }
        }

        public static IEnumerable<AnimatorState> EnumerateAllStates(this AnimatorStateMachine stateMachine)
        {
            var queue = new Queue<AnimatorStateMachine>();
            queue.Enqueue(stateMachine);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var subStateMachine in current.stateMachines)
                {
                    queue.Enqueue(subStateMachine.stateMachine);
                }
                foreach (var state in current.states.Select(s => s.state))
                {
                    yield return state;
                }
            }
        }

        public static IEnumerable<StateMachineBehaviour> EnumerateAllBehaviours(this AnimatorStateMachine stateMachine)
        {
            var queue = new Queue<AnimatorStateMachine>();
            queue.Enqueue(stateMachine);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var subStateMachine in current.stateMachines)
                {
                    queue.Enqueue(subStateMachine.stateMachine);
                }
                foreach (var behaviour in current.behaviours)
                {
                    yield return behaviour;
                }
                foreach (var state in current.states.Select(s => s.state))
                {
                    foreach (var behaviour in state.behaviours)
                    {
                        yield return behaviour;
                    }
                }
            }
        }

        public static List<AnimatorTransitionBase> EnumerateAllTransitions(this AnimatorStateMachine stateMachine)
        {
            var queue = new Queue<AnimatorStateMachine>();
            var stateTransitions = new List<AnimatorTransitionBase>();
            var transitions = new List<AnimatorTransitionBase>();
            queue.Enqueue(stateMachine);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                transitions.AddRange(current.entryTransitions);
                stateTransitions.AddRange(current.anyStateTransitions);
                stateTransitions.AddRange(current.states.SelectMany(s => s.state.transitions));
                foreach (var subStateMachine in current.stateMachines)
                {
                    queue.Enqueue(subStateMachine.stateMachine);
                    transitions.AddRange(current.GetStateMachineTransitions(subStateMachine.stateMachine));
                }
            }
            transitions.AddRange(stateTransitions);
            return transitions;
        }

        public static IEnumerable<AnimationClip> EnumerateAllClips(this Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
            }
            else if (motion is BlendTree tree)
            {
                var childNodes = tree.children;
                for (int i = 0; i < childNodes.Length; i++)
                {
                    if (childNodes[i].motion == null)
                    {
                        continue;
                    }
                    foreach (var childClip in childNodes[i].motion.EnumerateAllClips())
                    {
                        yield return childClip;
                    }
                }
            }
        }
    }

    public static class Vector3Extensions
    {
        public static Vector3 Multiply(this Vector3 vec, float x, float y, float z)
        {
            vec.x *= x;
            vec.y *= y;
            vec.z *= z;
            return vec;
        }
    }
}
#endif