#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Nur Editor/Development: erlaubt Screenshots aus einem bereits laufenden Play-Modus, ohne
    /// Unity neu zu starten. Ein Aufruf von <c>tools/request-screenshot.sh</c> schreibt den Zielpfad in
    /// lumiis-screenshot-request im Temp-Ordner (Linux /tmp, Windows %TEMP%); dieser Watcher nimmt den Screenshot beim nächsten Frame auf.
    /// Zusätzlich gibt es F12 im Spiel und einen Button im Asset-Testlabor.</summary>
    public sealed class ScreenshotRequestWatcher : MonoBehaviour
    {
        private static readonly string RequestPath = Path.Combine(Path.GetTempPath(), "lumiis-screenshot-request");
        private static readonly string DefaultTarget = Path.Combine(Path.GetTempPath(), "lumiis-screenshot.png");
        private float _nextPoll;

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;
            if (!File.Exists(RequestPath)) return;

            string target = DefaultTarget;
            try
            {
                string requested = File.ReadAllText(RequestPath).Trim();
                if (!string.IsNullOrEmpty(requested)) target = requested;
            }
            catch (IOException)
            {
                // Datei kann gerade noch geschrieben werden; nächster Poll versucht es erneut.
                return;
            }

            try
            {
                File.Delete(RequestPath);
            }
            catch (IOException)
            {
                return;
            }

            ScreenCapture.CaptureScreenshot(target, 1);
            Debug.Log("Screenshot über Request-Datei: " + target);
        }
    }
}
#endif
