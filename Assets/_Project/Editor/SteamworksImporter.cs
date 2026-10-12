using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.Networking;

namespace Voidward.EditorTools
{
    /// <summary>Copies steam_appid.txt next to every standalone build so Steam API init works.</summary>
    public class SteamAppIdBuildStep : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                string src = Path.GetFullPath("steam_appid.txt");
                if (!File.Exists(src)) return;
                string buildDir = Path.GetDirectoryName(report.summary.outputPath);
                File.Copy(src, Path.Combine(buildDir, "steam_appid.txt"), true);
                Debug.Log("[Steamworks] steam_appid.txt copied to build output.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Steamworks] steam_appid.txt copy failed: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Downloads Facepunch.Steamworks 2.5.2 from GitHub releases and stages it
    /// into Assets/Plugins/Steamworks. Runs in two steps so we can inspect the
    /// zip layout before wiring plugin importer settings.
    /// </summary>
    public static class SteamworksImporter
    {
        const string DownloadUrl = "https://github.com/Facepunch/Facepunch.Steamworks/releases/download/2.5.2/Facepunch.Steamworks.2.5.2.zip";
        static string ZipPath => Path.GetFullPath("Temp/facepunch_steamworks.zip");
        static string ExtractDir => Path.GetFullPath("Temp/facepunch_steamworks");

        [MenuItem("Voidward/Steamworks/Install Into Project")]
        public static void InstallIntoProject()
        {
            try
            {
                string src = Path.Combine(ExtractDir, "Release/Unity");
                if (!Directory.Exists(src))
                {
                    Debug.LogError("[SteamworksImporter] Run 'Download and Extract' first (missing " + src + ")");
                    return;
                }
                string dst = Path.GetFullPath("Assets/Plugins/Steamworks");
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                CopyTree(src, dst);

                // steam_appid.txt at project root (editor working directory) with test AppID 480.
                File.WriteAllText(Path.GetFullPath("steam_appid.txt"), "480");

                AssetDatabase.Refresh();

                // Enable the Steam code path.
                var target = UnityEditor.Build.NamedBuildTarget.Standalone;
                string defines = PlayerSettings.GetScriptingDefineSymbols(target);
                if (!defines.Contains("VOIDWARD_STEAM"))
                    PlayerSettings.SetScriptingDefineSymbols(target, string.IsNullOrEmpty(defines) ? "VOIDWARD_STEAM" : defines + ";VOIDWARD_STEAM");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Installed files:");
                foreach (var f in Directory.GetFiles(dst, "*", SearchOption.AllDirectories))
                    if (!f.EndsWith(".meta")) sb.AppendLine("  " + f.Substring(dst.Length + 1));
                sb.AppendLine("Define symbols now: " + PlayerSettings.GetScriptingDefineSymbols(target));
                Debug.Log(sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[SteamworksImporter] Install failed: " + e);
            }
        }

        static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".pdb") || name.EndsWith(".pdb.meta")) continue; // skip debug symbols
                File.Copy(f, Path.Combine(dst, name), true);
            }
            foreach (var d in Directory.GetDirectories(src))
                CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        [MenuItem("Voidward/Steamworks/Download and Extract")]
        public static void DownloadAndExtract()
        {
            try
            {
                if (!File.Exists(ZipPath))
                {
                    Debug.Log("[SteamworksImporter] Downloading " + DownloadUrl);
                    using (var req = UnityWebRequest.Get(DownloadUrl))
                    {
                        req.timeout = 120;
                        var op = req.SendWebRequest();
                        var deadline = DateTime.Now.AddSeconds(180);
                        while (!op.isDone && DateTime.Now < deadline)
                            System.Threading.Thread.Sleep(50);
                        if (!op.isDone || req.result != UnityWebRequest.Result.Success)
                        {
                            Debug.LogError("[SteamworksImporter] Download failed: " + req.error);
                            return;
                        }
                        File.WriteAllBytes(ZipPath, req.downloadHandler.data);
                    }
                    Debug.Log("[SteamworksImporter] Downloaded " + new FileInfo(ZipPath).Length + " bytes");
                }

                if (Directory.Exists(ExtractDir)) Directory.Delete(ExtractDir, true);
                Directory.CreateDirectory(ExtractDir);

                // Use the OS unzip tool (macOS: /usr/bin/unzip) to avoid extra assembly refs.
                var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/unzip", "-o -q \"" + ZipPath + "\" -d \"" + ExtractDir + "\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc.WaitForExit(60000);
                Debug.Log("[SteamworksImporter] unzip exit: " + proc.ExitCode);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Extracted files:");
                foreach (var f in Directory.GetFiles(ExtractDir, "*", SearchOption.AllDirectories))
                    sb.AppendLine("  " + f.Substring(ExtractDir.Length + 1));
                Debug.Log(sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[SteamworksImporter] Exception: " + e);
            }
        }
    }
}
