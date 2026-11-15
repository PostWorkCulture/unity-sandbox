// Reads a profiler capture (.raw, from a development build started with -profiler-enable -profiler-log-file <file>) and
// writes where the long frames went, as a tree of the samples over a threshold (progression chat, 15 Nov: finding the
// load pauses). Remote: call ProfileDump.Run C:\Dev\unity-sandbox\Logs\start.raw [minFrameMs] [minSampleMs]
// Output: Logs/profile-dump.txt
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace StreetLife.EditorTools
{
    public static class ProfileDump
    {
        public static string Run(string a)
        {
            var p = (a ?? "").Split(' ');
            string file = p.Length > 0 ? p[0] : "";
            float minFrame = p.Length > 1 ? float.Parse(p[1], CultureInfo.InvariantCulture) : 100;
            float minSample = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 15;
            if (!File.Exists(file)) return "no file " + file;
            if (!ProfilerDriver.LoadProfile(file, false)) return "couldn't load " + file;
            int f0 = ProfilerDriver.firstFrameIndex, f1 = ProfilerDriver.lastFrameIndex;
            var sb = new StringBuilder(); int shown = 0;
            for (int f = f0; f <= f1; f++)
            {
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (v == null || !v.valid) continue;
                    float ms = v.frameTimeMs;
                    if (ms < minFrame) continue;
                    shown++;
                    sb.AppendLine($"frame {f} ({f - f0} from the start): {ms:F0} ms, started {v.frameStartTimeMs / 1000:F1} s");
                    Walk(v, v.GetRootItemID(), 1, minSample, sb);
                    sb.AppendLine();
                }
            }
            var outp = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "profile-dump.txt");
            File.WriteAllText(outp, $"{file}: frames {f0}..{f1}, {shown} over {minFrame} ms\n\n" + sb);
            return $"{shown} long frames of {f1 - f0 + 1} written to {outp}";
        }

        static void Walk(HierarchyFrameDataView v, int id, int depth, float min, StringBuilder sb)
        {
            if (depth > 14) return;
            var kids = new List<int>(); v.GetItemChildren(id, kids);
            foreach (var k in kids)
            {
                float t = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime);
                if (t < min) continue;
                float self = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnSelfTime);
                sb.Append(' ', depth * 2).AppendLine($"{v.GetItemName(k)}  {t:F0} ms (self {self:F0})");
                Walk(v, k, depth + 1, min, sb);
            }
        }
    }
}
