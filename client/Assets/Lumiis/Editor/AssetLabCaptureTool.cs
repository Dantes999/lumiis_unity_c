using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lumiis.Editor
{
    /// <summary>Automatisierter Screenshot-Lauf für das Asset-Testlabor. Wird nur über
    /// `tools/capture-client-screenshots.sh` (bzw. -executeMethod) gestartet, nie im normalen Editor-Alltag.
    /// Der Runtime-Teil (AssetLabAutoStart, Startargument -lumiisAssetLab) schreibt die PNGs und am Ende
    /// eine Markerdatei; dieses Editor-Skript beendet danach Play-Modus und Editor.</summary>
    [InitializeOnLoad]
    public static class AssetLabCaptureTool
    {
        private const string SessionKey = "Lumiis.AssetLabCapture.Active";
        private static readonly string MarkerPath = Path.Combine(Path.GetTempPath(), "lumiis-assetlab-done");
        private const double TimeoutSeconds = 90.0;

        private static double _startedAt;
        private static bool _exitPending;

        static AssetLabCaptureTool()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SessionState.SetBool(SessionKey, true);
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);

            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            _startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                _startedAt = EditorApplication.timeSinceStartup;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(SessionKey, false))
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (EditorApplication.timeSinceStartup - _startedAt > TimeoutSeconds)
            {
                Debug.LogError("Asset-Testlabor-Screenshot: Timeout.");
                Shutdown(1);
                return;
            }

            if (!EditorApplication.isPlaying || !File.Exists(MarkerPath)) return;
            if (EditorApplication.timeSinceStartup - _startedAt < 2.0) return;

            Shutdown(0);
        }

        private static void Shutdown(int exitCode)
        {
            if (_exitPending) return;
            _exitPending = true;
            SessionState.SetBool(SessionKey, false);
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            // Die Screenshots liegen bereits auf der Platte; der Editor kann direkt beendet werden.
            EditorApplication.Exit(exitCode);
        }
    }
}
