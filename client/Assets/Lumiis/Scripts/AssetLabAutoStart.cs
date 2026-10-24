#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Nur für den automatisierten Screenshot-Lauf (Startargument `-lumiisAssetLab`): überspringt
    /// Login/Server, öffnet das Asset-Testlabor mit einer lokalen Vorschaufigur und legt PNGs unter /tmp ab.
    /// Im normalen Spiel wird diese Komponente nie erzeugt.</summary>
    public sealed class AssetLabAutoStart : MonoBehaviour
    {
        private const string MarkerPath = "/tmp/lumiis-assetlab-done";
        private GameClient _gameClient;

        public void Initialize(GameClient gameClient)
        {
            _gameClient = gameClient;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // GameClient.Initialize hat die Kataloge geladen; AssetTestLab baut seine UI einen Frame später.
            yield return null;
            yield return null;
            yield return null;

            _gameClient.DebugPrepareAssetLabLocalPlayer();
            yield return null;
            _gameClient.DebugOpenAssetLab();

            Camera mainCamera = Camera.main;
            if (mainCamera != null) mainCamera.orthographicSize = 4f;

            yield return new WaitForSeconds(0.5f);
            _gameClient.DebugSetAssetLabUpgrade(0);
            yield return null;
            ScreenCapture.CaptureScreenshot("/tmp/lumiis-asset-lab-plus0.png", 1);
            yield return new WaitForEndOfFrame();

            _gameClient.DebugSetAssetLabUpgrade(9);
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot("/tmp/lumiis-asset-lab-plus9.png", 1);
            yield return new WaitForEndOfFrame();

            _gameClient.DebugPlayAssetLabAttack();
            yield return new WaitForSeconds(0.18f);
            Debug.Log("AssetLab-Trail aktiv vor Screenshot: " + _gameClient.DebugAssetLabTrailEmitting
                + " Positionen=" + _gameClient.DebugAssetLabTrailPositionCount);
            ScreenCapture.CaptureScreenshot("/tmp/lumiis-asset-lab-attack.png", 1);
            yield return new WaitForEndOfFrame();

            _gameClient.DebugShowAssetLabIconGallery("weapon");
            yield return new WaitForSeconds(0.3f);
            ScreenCapture.CaptureScreenshot("/tmp/lumiis-asset-lab-icons.png", 1);
            yield return new WaitForEndOfFrame();

            _gameClient.DebugSetAssetLabUiVisible(false);
            _gameClient.DebugShowAssetLabMapPreview();
            if (mainCamera != null) mainCamera.orthographicSize = 12f;
            yield return new WaitForSeconds(0.3f);
            ScreenCapture.CaptureScreenshot("/tmp/lumiis-asset-lab-map.png", 1);
            yield return new WaitForEndOfFrame();

            _gameClient.DebugHideAssetLabMapPreview();
            _gameClient.DebugShowAssetLabGallery();
            if (mainCamera != null) mainCamera.orthographicSize = 3.2f;
            yield return new WaitForSeconds(0.4f);
            ScreenCapture.CaptureScreenshot("/tmp/lumiis-asset-lab-characters.png", 1);
            yield return new WaitForEndOfFrame();

            _gameClient.DebugCloseAssetLab();
            if (mainCamera != null) mainCamera.orthographicSize = 8f;
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot("/tmp/lumiis-hud.png", 1);
            yield return new WaitForEndOfFrame();

            yield return new WaitForSeconds(0.5f);
            File.WriteAllText(MarkerPath, "done");
            Debug.Log("Asset-Testlabor-Screenshots geschrieben: /tmp/lumiis-asset-lab-*.png");
        }
    }
}
#endif
