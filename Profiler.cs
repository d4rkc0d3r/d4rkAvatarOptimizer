#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace d4rkpl4y3r.Util
{
    static class Profiler
    {
        private static Dictionary<string, long> timeUsed = new Dictionary<string, long>();
        private static string name = "";
        private static long start;
        private static long unknownStart = DateTime.Now.Ticks;
        public static bool enabled = true;

        public static void Reset()
        {
            timeUsed.Clear();
            unknownStart = DateTime.Now.Ticks;
            name = "";
        }

        public static void StartNextSection(string name)
        {
            if (!enabled)
                return;
            if (name != "")
                EndSection();
            StartSection(name);
        }

        public static void StartSection(string name)
        {
            if (!enabled)
                return;
            Profiler.name = name;
            start = DateTime.Now.Ticks;
        }

        public static void EndSection()
        {
            if (!enabled)
                return;
            long end = DateTime.Now.Ticks;
            long v = 0;
            timeUsed.TryGetValue(name, out v);
            timeUsed[name] = v + end - start;
        }

        public static void PrintTimeUsed()
        {
            if (!enabled)
                return;
            long totalTime = DateTime.Now.Ticks - unknownStart;
            Debug.Log(string.Format("Total Time: {0:N3}s", new TimeSpan(totalTime).TotalSeconds));
            long unknownTime = totalTime - timeUsed.Values.Sum();
            timeUsed["unknown"] = unknownTime;
            double sum = (double)timeUsed.Values.Sum();
            int maxSectionNameLength = timeUsed.Keys.Select(n => n.Length).Max();
            foreach (var pair in timeUsed.OrderByDescending(p => p.Value))
            {
                double p = Math.Round(pair.Value / sum * 10000) / 100;
                Debug.Log(string.Format("Section {0} took {1:N2}% of time", pair.Key, p));
            }
        }
    }
}
#endif