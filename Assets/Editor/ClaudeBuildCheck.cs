// Temporary build-verification tool. Safe to delete.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ClaudeBuildCheck
{
    public static void BuildAndroid()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        Debug.Log($"[BUILDCHECK] scenes: {string.Join(", ", scenes)}");

        var outDir = Path.Combine(Path.GetTempPath(), "claude_build");
        Directory.CreateDirectory(outDir);
        var apk = Path.Combine(outDir, "colivri_check.apk");

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apk,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.Development,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[BUILDCHECK] result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} " +
                  $"size={s.totalSize} time={s.totalTime}");

        foreach (var step in report.steps)
            foreach (var msg in step.messages)
                if (msg.type == LogType.Error || msg.type == LogType.Exception)
                    Debug.Log($"[BUILDCHECK-ERR] {step.name}: {msg.content}");

        if (s.result != BuildResult.Succeeded)
            EditorApplication.Exit(2);
    }
}
