#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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