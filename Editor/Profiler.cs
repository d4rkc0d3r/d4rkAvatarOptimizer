#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace d4rkpl4y3r.AvatarOptimizer.Util
{
    static class Profiler
    {
        private static Dictionary<string, long> timeUsed = new Dictionary<string, long>();
        private static long lastReset = DateTime.Now.Ticks;
        private static Stack<(string name, long start)> stack = new Stack<(string name, long start)>();
        public static bool enabled = true;

        public static void Reset()
        {
            timeUsed.Clear();
            stack.Clear();
            lastReset = DateTime.Now.Ticks;
        }

        public static void StartNextSection(string name)
        {
            if (!enabled)
                return;
            EndSection();
            StartSection(name);
        }

        public static void StartSection(string name)
        {
            if (!enabled)
                return;
            var now = DateTime.Now.Ticks;
            if (stack.Count > 0)
            {
                var currentSection = stack.Peek();
                timeUsed.TryGetValue(currentSection.name, out long v);
                timeUsed[currentSection.name] = v + now - currentSection.start;
            }
            stack.Push((name, now));
        }

        public static void EndSection()
        {
            if (!enabled || stack.Count == 0)
                return;
            var now = DateTime.Now.Ticks;
            var currentSection = stack.Pop();
            timeUsed.TryGetValue(currentSection.name, out long v);
            timeUsed[currentSection.name] = v + now - currentSection.start;
            if (stack.Count > 0)
            {
                stack.Push((stack.Pop().name, now));
            }
        }

        public static List<string> FormatTimeUsed()
        {
            if (!enabled)
                return new List<string>();
            long totalTime = DateTime.Now.Ticks - lastReset;
            var result = new List<string>();
            result.Add(string.Format("Total Time: {0:N3}s", new TimeSpan(totalTime).TotalSeconds));
            long unknownTime = totalTime - timeUsed.Values.Sum();
            timeUsed["unknown"] = unknownTime;
            double sum = (double)timeUsed.Values.Sum();
            int maxSectionNameLength = timeUsed.Keys.Select(n => n.Length).Max();
            foreach (var pair in timeUsed.OrderByDescending(p => p.Value))
            {
                double p = Math.Round(pair.Value / sum * 10000) / 100;
                result.Add(string.Format("Section {0} took {1:N2}% of time", pair.Key, p));
            }
            return result;
        }

        public static void PrintTimeUsed()
        {
            foreach (var line in FormatTimeUsed())
            {
                Debug.Log(line);
            }
        }
    }
}
#endif