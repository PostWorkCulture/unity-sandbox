// Leaves Unity's own verdict where the Cowork chats can read it (they can't see the Console):
//  Logs/streetlife-compile.txt  every script compile: OK, or the errors and warnings with file and line
//  Logs/streetlife-console.txt  errors and exceptions logged in the editor or in play mode (last 300)
// Logs/ is git-ignored.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace StreetLife.EditorTools
{
    [InitializeOnLoad]
    static class StreetLifeDiagnostics
    {
        static readonly string Dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
        static readonly List<string> compile = new List<string>();
        static readonly Queue<string> console = new Queue<string>();
        static bool anyError;

        static StreetLifeDiagnostics()
        {
            CompilationPipeline.compilationStarted += _ => { compile.Clear(); anyError = false; };
            CompilationPipeline.assemblyCompilationFinished += (asm, msgs) =>
            {
                foreach (var m in msgs)
                {
                    if (m.type == CompilerMessageType.Error) anyError = true;
                    if (m.type == CompilerMessageType.Error || (m.type == CompilerMessageType.Warning && m.file != null && m.file.Contains("StreetLife")))
                        compile.Add($"{m.type}: {m.message}");
                }
            };
            CompilationPipeline.compilationFinished += _ => Write("streetlife-compile.txt",
                $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  Unity {Application.unityVersion}\n{(anyError ? "FAILED" : "OK")}\n" + string.Join("\n", compile) + "\n");
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            console.Enqueue($"{System.DateTime.Now:HH:mm:ss} {(EditorApplication.isPlaying ? "[play]" : "[editor]")} {type}: {msg}\n{FirstLines(stack, 6)}");
            while (console.Count > 300) console.Dequeue();
            var sb = new StringBuilder(); foreach (var l in console) sb.AppendLine(l);
            Write("streetlife-console.txt", sb.ToString());
        }

        static string FirstLines(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var lines = s.Split('\n'); return "  " + string.Join("\n  ", lines, 0, Mathf.Min(n, lines.Length));
        }

        static void Write(string name, string text)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, name), text); } catch { }
        }
    }
}
