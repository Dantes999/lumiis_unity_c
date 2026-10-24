using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Lumiis.Client.Tests.PlayMode
{
    /// <summary>PlayMode-/Screenshot-Test für das F10-Asset-Testlabor: baut ohne Server eine lokale
    /// Vorschaufigur auf, öffnet die UI, prüft Vorschau + Trainingspuppen und legt - wenn eine echte
    /// Grafikdevice vorhanden ist - Screenshots unter /tmp ab. Der normale PlayMode-Testlauf läuft mit
    /// `-nographics`; dann wird nur die Szene-/UI-Struktur geprüft, ohne Screenshot.</summary>
    public class AssetTestLabScreenshotTests
    {
        private GameObject _root;
        private GameObject _cameraGo;

        [UnityTest]
        public IEnumerator AssetLab_OpensWithPreviewAndDummies_CapturesWhenGraphicsAvailable()
        {
            _cameraGo = new GameObject("AssetLabTestCamera");
            Camera camera = _cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            camera.transform.position = new Vector3(5f, 5f, -10f);

            _root = new GameObject("AssetLabTestRoot");
            var netClient = _root.AddComponent<NetClient>();
            var gameClient = _root.AddComponent<GameClient>();
            gameClient.Initialize(netClient, camera);
            gameClient.DebugPrepareAssetLabLocalPlayer();

            // AssetTestLab baut seine UI-Toolkit-Wurzel einen Frame später auf.
            for (int i = 0; i < 10 && GameObject.Find("AssetTestLabUI") == null; i++)
                yield return null;

            gameClient.DebugOpenAssetLab();
            yield return null;
            yield return new WaitForSeconds(0.15f);

            Assert.IsNotNull(GameObject.Find("AssetLabPreview"), "Vorschaufigur fehlt.");
            Assert.IsNotNull(GameObject.Find("Trainingspuppe Links"), "Linke Trainingspuppe fehlt.");
            Assert.IsNotNull(GameObject.Find("Trainingspuppe Rechts"), "Rechte Trainingspuppe fehlt.");
            Assert.IsNotNull(GameObject.Find("Trainingspuppe Oben"), "Obere Trainingspuppe fehlt.");
            Assert.IsNotNull(Object.FindFirstObjectByType<UIDocument>(), "AssetTestLab-UI fehlt.");

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                yield return CaptureLab(camera, "lumiis-asset-lab-plus0.png");

                gameClient.DebugSetAssetLabUpgrade(9);
                yield return new WaitForSeconds(0.15f);
                yield return CaptureLab(camera, "lumiis-asset-lab-plus9.png");

                gameClient.DebugSetAssetLabUpgrade(0);
            }

            gameClient.DebugCloseAssetLab();
            yield return null;
        }

        /// <summary>Rendert Welt (Kamera) und UI (PanelSettings.targetTexture) getrennt in PNGs. Ein
        /// ScreenCapture der Game View geht im Batchmode nicht; RenderTextures funktionieren dagegen auch
        /// ohne geöffnetes Game View-Fenster. Die beiden Bilder kann man anschließend compositen.</summary>
        private static IEnumerator CaptureLab(Camera camera, string fileName)
        {
            const int width = 1280;
            const int height = 720;

            var worldRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = worldRt;
            camera.Render();
            Texture2D world = ReadTexture(worldRt);
            camera.targetTexture = null;
            Save(world, Path.ChangeExtension(fileName, ".world.png"));

            UIDocument document = Object.FindFirstObjectByType<UIDocument>();
            if (document != null && document.panelSettings != null)
            {
                var uiRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                uiRt.Create();
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = uiRt;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = previousActive;

                RenderTexture previous = document.panelSettings.targetTexture;
                document.panelSettings.targetTexture = uiRt;

                yield return null;
                yield return null;
                yield return null;

                Texture2D ui = ReadTexture(uiRt);
                Save(ui, Path.ChangeExtension(fileName, ".ui.png"));

                document.panelSettings.targetTexture = previous;
                Object.Destroy(ui);
                uiRt.Release();
                Object.Destroy(uiRt);
            }

            Object.Destroy(world);
            worldRt.Release();
            Object.Destroy(worldRt);
            yield return null;
        }

        private static Texture2D ReadTexture(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        private static void Save(Texture2D texture, string fileName)
        {
            string path = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Debug.Log("Asset-Testlabor-Screenshot: " + path);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_cameraGo != null) Object.Destroy(_cameraGo);

            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.name == "Floor" || go.name == "MapBoundaryBar" || go.name == "RangeRing")
                    Object.Destroy(go);
            }

            yield return null;
        }
    }
}
