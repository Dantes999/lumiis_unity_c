using System;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Erzeugt beim Laden der Szene das komplette Client-Setup per Code (kein Inspector-Verkabeln,
    /// keine Szenenänderung).</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            Application.runInBackground = true;

            Camera camera = EnsureOrthographicMainCamera();

            var root = new GameObject("Lumiis");
            var netClient = root.AddComponent<NetClient>();
            var gameClient = root.AddComponent<GameClient>();
            gameClient.Initialize(netClient, camera);

            var hudController = root.AddComponent<HudController>();
            hudController.Initialize(gameClient);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Nur für automatisierte Screenshot-Läufe (tools/capture-client-screenshots.sh): Login
            // überspringen und direkt eine lokale Testlabor-Vorschau starten.
            if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-lumiisAssetLab"))
            {
                var autoLab = root.AddComponent<AssetLabAutoStart>();
                autoLab.Initialize(gameClient);
                return;
            }
#endif

            var loginController = root.AddComponent<LoginController>();
            loginController.Initialize(netClient, gameClient);
        }

        private static Camera EnsureOrthographicMainCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                camera = cameraGo.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.1f);

            Vector3 position = camera.transform.position;
            camera.transform.position = new Vector3(position.x, position.y, -10f);

            return camera;
        }
    }
}
