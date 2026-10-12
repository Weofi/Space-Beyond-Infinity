using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Voidward.EditorTools
{
    /// <summary>One-click standalone builds (Voidward menu).</summary>
    public static class BuildMenu
    {
        [MenuItem("Voidward/Build/Windows (x64)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Voidward.exe");

        [MenuItem("Voidward/Build/macOS")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/macOS/Voidward.app");

        [MenuItem("Voidward/Build/macOS (Development)")]
        public static void BuildMacDev() => Build(BuildTarget.StandaloneOSX, "Builds/macOS-Dev/Voidward.app", BuildOptions.Development);

        [MenuItem("Voidward/Build/Windows (x64, Development)")]
        public static void BuildWindowsDev() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows-Dev/Voidward.exe", BuildOptions.Development);

        static void Build(BuildTarget target, string path, BuildOptions extra = BuildOptions.None)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
            {
                Debug.LogError($"[Voidward] Build target {target} is not installed. Add it via Unity Hub > Installs > Add modules.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ContentBuilder.ScenePath },
                locationPathName = path,
                target = target,
                options = extra
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
                Debug.Log($"[Voidward] Build succeeded: {path} ({s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0}s, warnings {s.totalWarnings})");
            else
                Debug.LogError($"[Voidward] Build {s.result}: {s.totalErrors} errors");
        }
    }
}
