using UnityEngine;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>Hält den Skalierungsfaktor einer PanelSettings-Instanz bei Fenstergrößen-Änderungen aktuell
    /// (siehe UiSkin.ComputeScale). Sitzt am selben GameObject wie das UIDocument.</summary>
    public sealed class UiScaler : MonoBehaviour
    {
        private PanelSettings _settings;
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        public void Initialize(PanelSettings settings)
        {
            _settings = settings;
            Apply();
        }

        private void Update()
        {
            if (Screen.width != _lastWidth || Screen.height != _lastHeight) Apply();
        }

        private void Apply()
        {
            if (_settings == null) return;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            _settings.scale = UiSkin.ComputeScale(_lastWidth, _lastHeight);
        }
    }
}
