using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Lumiis.Client.Tests.PlayMode
{
    /// <summary>PlayMode-Test für das M7-HUD: baut die UI-Toolkit-Wurzel ohne Server auf und prüft, dass
    /// Spieler-/Chat-Elemente vorhanden sind. Die Werte selbst werden im laufenden Spiel aktualisiert.</summary>
    public class HudControllerPlayModeTests
    {
        private GameObject _root;
        private GameObject _cameraGo;

        [UnityTest]
        public IEnumerator Hud_BuildsPlayerAndChatElementsWhileInGame()
        {
            _cameraGo = new GameObject("HudTestCamera");
            Camera camera = _cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.transform.position = new Vector3(5f, 5f, -10f);

            _root = new GameObject("HudTestRoot");
            var netClient = _root.AddComponent<NetClient>();
            var gameClient = _root.AddComponent<GameClient>();
            gameClient.Initialize(netClient, camera);

            var hud = _root.AddComponent<HudController>();
            hud.Initialize(gameClient);
            gameClient.DebugPrepareAssetLabLocalPlayer();

            GameObject hudGo = null;
            for (int i = 0; i < 10 && hudGo == null; i++)
            {
                yield return null;
                hudGo = GameObject.Find("HudUI");
            }

            Assert.IsNotNull(hudGo, "HudUI wurde nicht erzeugt.");
            UIDocument document = hudGo.GetComponent<UIDocument>();
            Assert.IsNotNull(document, "HudUI hat kein UIDocument.");
            Assert.IsNotNull(document.rootVisualElement.Q<Label>("hud-player-name"), "Spielername fehlt.");
            Assert.IsNotNull(document.rootVisualElement.Q<VisualElement>("hud-hp-fill"), "HP-Balken fehlt.");
            Assert.IsNotNull(document.rootVisualElement.Q<Label>("hud-skill-1"), "Skill 1 fehlt.");
            Assert.IsNotNull(document.rootVisualElement.Q<ScrollView>("hud-chat-log"), "Chat-Log fehlt.");
            Assert.IsNotNull(document.rootVisualElement.Q<TextField>("hud-chat-input"), "Chat-Eingabe fehlt.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_cameraGo != null) Object.Destroy(_cameraGo);

            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.name == "Floor" || go.name == "MapBoundaryBar" || go.name == "RangeRing" || go.name == "HudUI")
                    Object.Destroy(go);
            }
            yield return null;
        }
    }
}
