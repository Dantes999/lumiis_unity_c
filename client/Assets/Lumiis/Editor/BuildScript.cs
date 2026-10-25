using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Lumiis.Editor
{
    /// <summary>Batchmode-Buildskript, aufgerufen über tools/build-client.sh:
    /// Unity -batchmode -quit -executeMethod Lumiis.Editor.BuildScript.BuildDevelopment
    /// Der Editor muss dafür geschlossen sein (Projekt-Lock). Liegt im Ordner "Editor" - von Unity automatisch
    /// aus allen Player-Builds ausgeschlossen (nicht nur per #if UNITY_EDITOR).</summary>
    public static class BuildScript
    {
        public static void BuildDevelopment()
        {
            string platformFolder = GetPlatformFolderName(EditorUserBuildSettings.activeBuildTarget);
            string fileName = "Lumiis" + GetExecutableExtension(EditorUserBuildSettings.activeBuildTarget);
            string outputPath = Path.Combine("Builds", platformFolder, fileName);

            string[] scenes = new string[EditorBuildSettings.scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
                scenes[i] = EditorBuildSettings.scenes[i].path;

            if (scenes.Length == 0)
            {
                Debug.LogError("BuildScript: keine Szenen in EditorBuildSettings - Build abgebrochen.");
                EditorApplication.Exit(1);
                return;
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = EditorUserBuildSettings.activeBuildTarget,
                options = BuildOptions.Development
            };

            // Marker für die Fortschrittsanzeige in tools/build-client.sh.
            Debug.Log("LUMIIS-BUILD: Player bauen (" + scenes.Length + " Szene(n), Ziel " + options.target + ")");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("BuildScript: Build fehlgeschlagen (" + report.summary.result + ")");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("LUMIIS-BUILD: Player fertig -> " + outputPath);
            EditorApplication.Exit(0);
        }

        private static string GetPlatformFolderName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneWindows:
                    return "windows";
                case BuildTarget.StandaloneLinux64:
                    return "linux";
                case BuildTarget.StandaloneOSX:
                    return "macos";
                default:
                    return target.ToString().ToLowerInvariant();
            }
        }

        private static string GetExecutableExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneWindows:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                default:
                    return ""; // Linux: keine Endung
            }
        }
    }
}
