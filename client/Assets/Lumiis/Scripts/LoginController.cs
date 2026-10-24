using System.Collections;
using Lumiis.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>Reine UI-Verdrahtung für Login/Registrierung/Charakterauswahl - hält eine LoginFlow-Instanz,
    /// verbindet Button-Klicks mit deren Methoden und zeigt LoginFlow.State/-ErrorMessage an. Jede
    /// Entscheidungslogik (Validierung, Fehlertexte, Zustandswechsel) lebt in LoginFlow (siehe dort), nicht
    /// hier - das macht sie ohne Unity per dotnet test prüfbar (Lumiis.Tests.csproj Compile-Include).
    ///
    /// Eigenes UIDocument (code-first erzeugt, kein Inspector-Verkabeln), schaltet zwischen Login.uxml und
    /// CharacterSelect.uxml um. Das bestehende Debug-Test-Panel (GameClient) bleibt unverändert und wird erst
    /// nach erfolgreichem Join sichtbar, weil GameClient seine eigene, von diesem Controller unabhängige
    /// InGame-Erkennung behält.</summary>
    public sealed class LoginController : MonoBehaviour
    {
        private const string UsernamePrefsKey = "Lumiis.LastUsername";

        private NetClient _netClient;
        private GameClient _gameClient;
        private LoginFlow _loginFlow;

        private UIDocument _uiDocument;
        private VisualElement _loginRoot;
        private VisualElement _characterSelectRoot;

        private TextField _usernameField;
        private TextField _passwordField;
        private Button _loginButton;
        private Button _registerButton;
        private Label _loginStatusLabel;
        private Label _loginErrorLabel;
        private TextField _authUrlField;
        private TextField _serverHostField;
        private TextField _serverPortField;

        private ScrollView _characterListView;
        private TextField _newCharacterNameField;
        private Button _createCharacterButton;
        private Button _logoutButton;
        private Label _characterSelectStatusLabel;
        private Label _characterSelectErrorLabel;

        // ---- escape-menu (siehe docs/tasks/escape-menu.md) - eigene, dritte UI-Wurzel im selben UIDocument
        // wie Login/CharacterSelect (siehe BuildUi). Bewusst NICHT über LoginFlowState gesteuert (das Menü
        // ist eine reine Overlay-Sichtbarkeit ÜBER dem laufenden Spiel, kein eigener Login-Zustand) - eigenes
        // bool, nur während State == InGame per Escape erreichbar (siehe Update).
        private VisualElement _escapeMenuRoot;
        private bool _escapeMenuOpen;

        /// <summary>authClient optional für PlayMode-Tests (siehe client/Assets/Lumiis/Tests/PlayMode) - im
        /// normalen Spielbetrieb (Bootstrap.cs) bleibt der Parameter weg, dann kommt die echte
        /// UnityWebRequestAuthClient gegen Lumiis.Auth zum Einsatz.</summary>
        public void Initialize(NetClient netClient, GameClient gameClient, IAuthClient authClient = null)
        {
            _netClient = netClient;
            _gameClient = gameClient;
            _loginFlow = new LoginFlow(authClient ?? new UnityWebRequestAuthClient());

            _netClient.PacketReceived += OnPacketReceived;
            _netClient.Disconnected += OnNetDisconnected;

            // Nicht sofort bauen: Initialize() läuft über Bootstraps RuntimeInitializeOnLoadMethod
            // (AfterSceneLoad), also vor dem ersten echten Frame. UI Toolkit ermittelt die Panel-Größe für
            // Layout UND Klick-Trefferbereich einmalig aus Screen.width/height beim Aktivieren des
            // UIDocument - ist die Game-View zu diesem sehr frühen Zeitpunkt noch nicht auf ihre
            // tatsächliche Größe eingeschwungen, zeichnet Unity später zwar an der richtigen Bildschirm-
            // position, Layout/Trefferbereich bleiben aber auf der ursprünglich falschen Größe hängen
            // (Buttons kaum klickbar, TextFields kollabiert/unsichtbar - ohne jede Fehlermeldung). Einen
            // Frame warten behebt das.
            StartCoroutine(BuildUiNextFrame());
        }

        private IEnumerator BuildUiNextFrame()
        {
            yield return null;
            BuildUi();
            ApplyLoginFlowState();
        }

        // ---- UI-Aufbau (code-first, kein Inspector-Verkabeln) ----

        private void BuildUi()
        {
            var go = new GameObject("LoginUI");
            go.SetActive(false); // Erst konfigurieren, dann aktivieren - UIDocument.OnEnable soll nicht mit leeren Panel-Settings laufen.

            _uiDocument = go.AddComponent<UIDocument>();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/DefaultRuntimeTheme");
            _uiDocument.panelSettings = panelSettings;

            // Erst jetzt aktivieren: UIDocument.OnEnable() erzeugt rootVisualElement aus den PanelSettings,
            // das braucht ein aktives GameObject. Vorher zugewiesene PanelSettings sorgen dafür, dass
            // OnEnable nicht mit leeren/Default-Settings läuft.
            go.SetActive(true);

            VisualTreeAsset loginAsset = Resources.Load<VisualTreeAsset>("UI/Login");
            VisualTreeAsset characterSelectAsset = Resources.Load<VisualTreeAsset>("UI/CharacterSelect");
            VisualTreeAsset escapeMenuAsset = Resources.Load<VisualTreeAsset>("UI/EscapeMenu");
            _loginRoot = loginAsset.CloneTree();
            _characterSelectRoot = characterSelectAsset.CloneTree();
            _escapeMenuRoot = escapeMenuAsset.CloneTree();
            _uiDocument.rootVisualElement.Add(_loginRoot);
            _uiDocument.rootVisualElement.Add(_characterSelectRoot);
            // escape-menu ABSICHTLICH als dritter Eintrag NACH Login/CharacterSelect angehängt - die
            // bestehenden LoginControllerPlayModeTests greifen per root[0]/root[1] auf genau diese beiden
            // zu, eine andere Reihenfolge würde sie brechen.
            _uiDocument.rootVisualElement.Add(_escapeMenuRoot);
            // Sofort auf den Anfangszustand (LoggedOut) einschränken, statt sich auf den direkt folgenden
            // ApplyLoginFlowState()-Aufruf des Callers zu verlassen - BuildUi() soll für sich allein schon nie
            // zwei sichtbare Wurzeln gleichzeitig erzeugen.
            _characterSelectRoot.style.display = DisplayStyle.None;
            _escapeMenuRoot.style.display = DisplayStyle.None;

            _usernameField = _loginRoot.Q<TextField>("username-field");
            _passwordField = _loginRoot.Q<TextField>("password-field");
            _loginButton = _loginRoot.Q<Button>("login-button");
            _registerButton = _loginRoot.Q<Button>("register-button");
            _loginStatusLabel = _loginRoot.Q<Label>("login-status");
            _loginErrorLabel = _loginRoot.Q<Label>("login-error");
            _authUrlField = _loginRoot.Q<TextField>("auth-url-field");
            _serverHostField = _loginRoot.Q<TextField>("server-host-field");
            _serverPortField = _loginRoot.Q<TextField>("server-port-field");

            _characterListView = _characterSelectRoot.Q<ScrollView>("character-list");
            _newCharacterNameField = _characterSelectRoot.Q<TextField>("new-character-name-field");
            _createCharacterButton = _characterSelectRoot.Q<Button>("create-character-button");
            _logoutButton = _characterSelectRoot.Q<Button>("logout-button");
            _characterSelectStatusLabel = _characterSelectRoot.Q<Label>("character-select-status");
            _characterSelectErrorLabel = _characterSelectRoot.Q<Label>("character-select-error");

            _usernameField.value = PlayerPrefs.GetString(UsernamePrefsKey, "");

            _loginButton.clicked += OnLoginButtonClicked;
            _registerButton.clicked += OnRegisterButtonClicked;
            _createCharacterButton.clicked += OnCreateCharacterButtonClicked;
            _logoutButton.clicked += OnLogoutButtonClicked;

            _escapeMenuRoot.Q<Button>("escape-switch-character-button").clicked += SwitchCharacter;
            _escapeMenuRoot.Q<Button>("escape-logout-button").clicked += Logout;
            _escapeMenuRoot.Q<Button>("escape-quit-button").clicked += QuitGame;
        }

        private void Update()
        {
            // Nur während eines laufenden Spiels erreichbar - Login/Charakterauswahl haben kein Escape-Menü.
            if (_loginFlow.State != LoginFlowState.InGame) return;
            if (Keyboard.current == null) return;
            if (_gameClient != null && _gameClient.IsChatInputFocused) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame) ToggleEscapeMenu();
        }

        private void ToggleEscapeMenu()
        {
            if (_escapeMenuOpen) CloseEscapeMenu();
            else OpenEscapeMenu();
        }

        private void OpenEscapeMenu()
        {
            _escapeMenuOpen = true;
            _escapeMenuRoot.style.display = DisplayStyle.Flex;
            _gameClient.SetGameplayInputLocked(true);
        }

        /// <summary>Auch von ApplyLoginFlowState() bei JEDEM Zustandswechsel aufgerufen (siehe dort) - ein
        /// offenes Menü darf nie stehen bleiben, wenn sich der Login-Zustand ändert (z. B. nach
        /// SwitchCharacter/Logout, oder bei einem tatsächlich unfreiwilligen Disconnect während offenem
        /// Menü). No-op, falls ohnehin schon geschlossen.</summary>
        private void CloseEscapeMenu()
        {
            if (!_escapeMenuOpen) return;
            _escapeMenuOpen = false;
            _escapeMenuRoot.style.display = DisplayStyle.None;
            _gameClient.SetGameplayInputLocked(false);
        }

        // ---- LoginFlow -> UI: einziger Ort, der State/ErrorMessage liest und den Bildschirm danach ausrichtet.
        // Löst nebenbei den M2c-Fehler "Fehlermeldung wird sofort wieder gelöscht" strukturell: ShowLogin()/
        // ShowCharacterSelect() fassen die Fehlertexte selbst nicht mehr an, hier wird immer erst umgeschaltet
        // und danach exakt der aktuelle LoginFlow-Text gesetzt - keine Reihenfolge-Falle mehr möglich. ----

        private void ApplyLoginFlowState()
        {
            // Jeder Zustandswechsel schließt ein offenes escape-menu (z. B. nach SwitchCharacter/Logout,
            // oder bei einem tatsächlich unfreiwilligen Disconnect während offenem Menü) - kein Zustand
            // außer InGame soll es je sichtbar haben.
            CloseEscapeMenu();

            switch (_loginFlow.State)
            {
                case LoginFlowState.LoggedOut:
                    ShowLogin();
                    _passwordField.value = "";
                    SetLoginError(_loginFlow.LoginErrorMessage);
                    break;

                case LoginFlowState.LoggingIn:
                    // Wird während eines laufenden await nie sichtbar neu gezeichnet (kein Polling), bleibt
                    // hier nur der Vollständigkeit halber - Interactable/Statustext setzen die Button-Handler
                    // selbst vor dem await.
                    break;

                case LoginFlowState.CharacterSelect:
                    ShowCharacterSelect();
                    SetCharacterSelectError(_loginFlow.CharacterSelectErrorMessage);
                    PopulateCharacterList();
                    if (!string.IsNullOrEmpty(_loginFlow.Token))
                    {
                        PlayerPrefs.SetString(UsernamePrefsKey, (_usernameField.value ?? "").Trim());
                        PlayerPrefs.Save();
                    }
                    break;

                case LoginFlowState.Connecting:
                    BeginConnect();
                    break;

                case LoginFlowState.InGame:
                    HideAll();
                    break;
            }
        }

        private void ShowLogin()
        {
            _loginRoot.style.display = DisplayStyle.Flex;
            _characterSelectRoot.style.display = DisplayStyle.None;
            SetLoginInteractable(true);
            _loginStatusLabel.text = "";
        }

        private void ShowCharacterSelect()
        {
            _loginRoot.style.display = DisplayStyle.None;
            _characterSelectRoot.style.display = DisplayStyle.Flex;
            SetCharacterSelectInteractable(true);
            _characterSelectStatusLabel.text = "";
        }

        private void HideAll()
        {
            _loginRoot.style.display = DisplayStyle.None;
            _characterSelectRoot.style.display = DisplayStyle.None;
        }

        // ---- Login/Registrierung ----

        private async void OnLoginButtonClicked()
        {
            SetLoginInteractable(false);
            SetLoginError("");
            _loginStatusLabel.text = "Melde an ...";
            await _loginFlow.TryLoginAsync(NormalizedAuthUrl(), _usernameField.value, _passwordField.value);
            ApplyLoginFlowState();
        }

        private async void OnRegisterButtonClicked()
        {
            SetLoginInteractable(false);
            SetLoginError("");
            _loginStatusLabel.text = "Registriere ...";
            await _loginFlow.TryRegisterAsync(NormalizedAuthUrl(), _usernameField.value, _passwordField.value);
            ApplyLoginFlowState();
        }

        // ---- Charakterauswahl ----

        private void PopulateCharacterList()
        {
            _characterListView.Clear();
            foreach (CharacterInfo character in _loginFlow.Characters)
            {
                CharacterInfo captured = character; // Schleifenvariable für den Closure einfangen.
                var button = new Button(() => OnCharacterSelected(captured)) { text = captured.Name };
                button.AddToClassList("character-button");
                _characterListView.Add(button);
            }
        }

        private void OnCharacterSelected(CharacterInfo character)
        {
            _loginFlow.SelectCharacter(character);
            ApplyLoginFlowState();
        }

        private async void OnCreateCharacterButtonClicked()
        {
            SetCharacterSelectInteractable(false);
            SetCharacterSelectError("");
            _characterSelectStatusLabel.text = "Erstelle Charakter ...";
            await _loginFlow.CreateCharacterAsync(NormalizedAuthUrl(), _newCharacterNameField.value);
            if (_loginFlow.State == LoginFlowState.Connecting) _newCharacterNameField.value = "";
            ApplyLoginFlowState();
        }

        private void OnLogoutButtonClicked()
        {
            _loginFlow.Logout();
            ApplyLoginFlowState();
        }

        // ---- escape-menu: Charakter wechseln / Ausloggen / Beenden (siehe docs/tasks/escape-menu.md) ----

        /// <summary>Trennt die aktive Spielverbindung, behält das Token, wechselt zur Charakterauswahl und
        /// lädt die Liste neu. Zustand wird VOR dem Trennen gesetzt (siehe LoginFlow.SwitchCharacter-
        /// Kommentar), damit der anschließende NetClient.Stop()-Disconnect keine Token-Verwerfung auslöst.</summary>
        public async void SwitchCharacter()
        {
            _loginFlow.SwitchCharacter();
            ApplyLoginFlowState();
            _netClient.Stop();
            await _loginFlow.RefreshCharactersAsync(NormalizedAuthUrl());
            ApplyLoginFlowState();
        }

        /// <summary>Trennt die Verbindung und verwirft das Token - reine Wiederverwendung des bestehenden
        /// LoginFlow.Logout() (identisch zum unfreiwilligen Disconnect-Pfad), Zustand ebenfalls VOR dem
        /// Trennen gesetzt (siehe SwitchCharacter-Kommentar).</summary>
        public void Logout()
        {
            _loginFlow.Logout();
            ApplyLoginFlowState();
            _netClient.Stop();
        }

        /// <summary>Im Editor stoppt das nur den Play-Modus statt den ganzen Editor zu beenden.</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- Verbindung zum Spielserver (reines Transport-/UI-Wissen, siehe LoginFlow.CancelConnect) ----

        private void BeginConnect()
        {
            if (!int.TryParse(_serverPortField.value, out int port))
            {
                _loginFlow.CancelConnect("Ungültiger Server-Port.");
                ApplyLoginFlowState();
                return;
            }

            SetCharacterSelectInteractable(false);
            SetCharacterSelectError("");
            _characterSelectStatusLabel.text = "Verbinde ...";

            _gameClient.PrepareJoin(_loginFlow.SelectedCharacterName);
            bool started = _netClient.Connect(_serverHostField.value, port, _loginFlow.Token, _loginFlow.SelectedCharacterId);
            if (!started)
            {
                _loginFlow.CancelConnect("Verbindung konnte nicht gestartet werden.");
                ApplyLoginFlowState();
            }
        }

        private void OnPacketReceived(IPacket packet)
        {
            if (packet is JoinAcceptedPacket)
            {
                _loginFlow.OnJoinAccepted();
                ApplyLoginFlowState();
                return;
            }

            if (packet is JoinRejectedPacket rejected)
            {
                _loginFlow.OnJoinRejected(rejected.Reason);
                ApplyLoginFlowState();
            }
        }

        private void OnNetDisconnected(string reason)
        {
            _loginFlow.OnDisconnected(reason);
            ApplyLoginFlowState();
        }

        // ---- UI-Hilfsfunktionen ----

        private void SetLoginError(string message)
        {
            _loginErrorLabel.text = message ?? "";
        }

        private void SetCharacterSelectError(string message)
        {
            _characterSelectErrorLabel.text = message ?? "";
        }

        private void SetLoginInteractable(bool interactable)
        {
            _usernameField.SetEnabled(interactable);
            _passwordField.SetEnabled(interactable);
            _loginButton.SetEnabled(interactable);
            _registerButton.SetEnabled(interactable);
        }

        private void SetCharacterSelectInteractable(bool interactable)
        {
            _characterListView.SetEnabled(interactable);
            _newCharacterNameField.SetEnabled(interactable);
            _createCharacterButton.SetEnabled(interactable);
            _logoutButton.SetEnabled(interactable);
        }

        private string NormalizedAuthUrl()
        {
            string url = (_authUrlField.value ?? "").Trim();
            if (string.IsNullOrEmpty(url)) url = "http://127.0.0.1:5080";
            if (!url.EndsWith("/")) url += "/";
            return url;
        }
    }
}
