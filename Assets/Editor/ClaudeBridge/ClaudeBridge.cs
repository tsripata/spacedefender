// Editor bridge: lets the project be refreshed, checked and built without clicking in the Editor.
// Drop a request file into <project>/ClaudeBridge/requests/ :
//   refresh.req  -> AssetDatabase.Refresh() (recompiles changed scripts)
//   build.req    -> WebGL build into <project>/WebBuild
// Results are written to <project>/ClaudeBridge/ (status.json, compile.log, editor.log, build.log).
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
public static class ClaudeBridge
{
    static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static readonly string Dir = Path.Combine(Root, "ClaudeBridge");
    static readonly string Req = Path.Combine(Dir, "requests");
    static double _next;
    static int _errors;
    static bool _building;

    static ClaudeBridge()
    {
        Directory.CreateDirectory(Req);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        CompilationPipeline.compilationStarted -= OnCompileStart;
        CompilationPipeline.compilationStarted += OnCompileStart;
        CompilationPipeline.assemblyCompilationFinished -= OnAssembly;
        CompilationPipeline.assemblyCompilationFinished += OnAssembly;
        CompilationPipeline.compilationFinished -= OnCompileDone;
        CompilationPipeline.compilationFinished += OnCompileDone;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        // keep play mode (and the web build) running when the window isn't focused
        if (!PlayerSettings.runInBackground) PlayerSettings.runInBackground = true;
        WriteStatus("loaded");
    }

    static void OnCompileStart(object _)
    {
        _errors = 0;
        File.WriteAllText(Path.Combine(Dir, "compile.log"), $"compile started {DateTime.Now:O}\n");
    }

    static void OnAssembly(string asm, CompilerMessage[] msgs)
    {
        var sb = new StringBuilder();
        foreach (var m in msgs)
        {
            if (m.type == CompilerMessageType.Error) _errors++;
            if (m.type == CompilerMessageType.Error || m.type == CompilerMessageType.Warning)
                sb.AppendLine($"{m.type}: {m.file}({m.line},{m.column}): {m.message}");
        }
        sb.AppendLine($"assembly done: {Path.GetFileName(asm)}");
        File.AppendAllText(Path.Combine(Dir, "compile.log"), sb.ToString());
    }

    static void OnCompileDone(object _)
    {
        WriteStatus(_errors == 0 ? "compiled-ok" : "compiled-errors");
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Log) return;
        try
        {
            File.AppendAllText(Path.Combine(Dir, "editor.log"),
                $"[{DateTime.Now:HH:mm:ss}] {type}: {msg}\n{(type == LogType.Exception ? stack + "\n" : "")}");
        }
        catch { }
    }

    static void WriteStatus(string state, string extra = "")
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "status.json"),
                "{\"state\":\"" + state + "\",\"errors\":" + _errors + ",\"time\":\"" + DateTime.Now.ToString("O") +
                "\",\"compiling\":" + (EditorApplication.isCompiling ? "true" : "false") +
                ",\"extra\":\"" + extra.Replace("\"", "'").Replace("\n", " ") + "\"}");
        }
        catch { }
    }

    static void Tick()
    {
        // Unity doesn't tick play mode while the Editor window is unfocused; force it during smoke tests.
        if (EditorApplication.isPlaying && File.Exists(Path.Combine(Dir, "play.deadline")))
            EditorApplication.QueuePlayerLoopUpdate();
        if (_building || EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 2.0;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        // play-mode smoke test: play.req -> enter play mode with autotest.flag; exit on done or deadline
        var deadline = Path.Combine(Dir, "play.deadline");
        if (EditorApplication.isPlaying)
        {
            bool done = File.Exists(Path.Combine(Dir, "autotest.done"));
            bool late = File.Exists(deadline) && DateTime.Now > DateTime.Parse(File.ReadAllText(deadline));
            var stop = Path.Combine(Req, "stop.req");
            if (File.Exists(stop)) { File.Delete(stop); late = true; }
            if (done || late)
            {
                if (File.Exists(deadline)) File.Delete(deadline);
                WriteStatus(done ? "play-done" : "play-timeout");
                EditorApplication.ExitPlaymode();
            }
            return;
        }
        var play = Path.Combine(Req, "play.req");
        if (File.Exists(play))
        {
            File.Delete(play);
            var done = Path.Combine(Dir, "autotest.done");
            if (File.Exists(done)) File.Delete(done);
            File.WriteAllText(Path.Combine(Dir, "autotest.flag"), "1");
            File.WriteAllText(deadline, DateTime.Now.AddMinutes(6).ToString("O"));
            WriteStatus("playing");
            EditorApplication.EnterPlaymode();
            return;
        }

        var refresh = Path.Combine(Req, "refresh.req");
        if (File.Exists(refresh))
        {
            File.Delete(refresh);
            WriteStatus("refreshing");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!EditorApplication.isCompiling) WriteStatus(_errors == 0 ? "refreshed" : "compiled-errors");
            return;
        }

        var build = Path.Combine(Req, "build.req");
        if (File.Exists(build))
        {
            File.Delete(build);
            BuildWeb();
        }
    }

    [MenuItem("SpaceDefender/Build Web")]
    public static void BuildWeb()
    {
        _building = true;
        try
        {
            WriteStatus("building");
            AssetDatabase.Refresh();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            PlayerSettings.companyName = "tsripata";
            PlayerSettings.productName = "Space Defender";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true; // needed for GitHub Pages
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/SampleScene.unity" };

            var outDir = Path.Combine(Root, "WebBuild");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            var s = report.summary;
            var sb = new StringBuilder();
            sb.AppendLine($"result: {s.result}  errors: {s.totalErrors}  warnings: {s.totalWarnings}  size: {s.totalSize}  time: {s.totalTime}");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        sb.AppendLine($"{m.type}: {m.content}");
            File.WriteAllText(Path.Combine(Dir, "build.log"), sb.ToString());
            WriteStatus(s.result == BuildResult.Succeeded ? "build-ok" : "build-failed", s.result.ToString());
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Dir, "build.log"), e.ToString());
            WriteStatus("build-failed", e.Message);
        }
        finally { _building = false; }
    }
}
