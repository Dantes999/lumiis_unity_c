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
        private Button _startGameButton;
        private VisualElement _previewFigure;
        private Label _selectedNameLabel;
        private CharacterInfo? _selectedCharacter;
        private Label _selectedInfoLabel;
        private LookCatalogClient _lookCatalog;
        private Label _lookBodyValue;
        private Label _lookHairValue;
        private VisualElement _lookColorSwatch;
        private int _lookBodyIndex;
        private int _lookHairIndex;
        private int _lookColorIndex; // 0-basiert in hairColors, auf der Leitung +1
        private bool _previewingNewLook;
        private HudController _hud;

        // ---- escape-menu (siehe docs/tasks/escape-menu.md) - eigene, dritte UI-Wurzel im selben UIDocument
        // wie Login/CharacterSelect (siehe BuildUi). Bewusst NICHT über LoginFlowState gesteuert (das Menü
        // ist eine reine Overlay-Sichtbarkeit ÜBER dem laufenden Spiel, kein eigener Login-Zustand) - eigenes
        // bool, nur während State == InGame per Escape erreichbar (siehe Update).
        private VisualElement _escapeMenuRoot;
        private bool _escapeMenuOpen;

        /// <summary>authClient optional für PlayMode-Tests (siehe client/Assets/Lumiis/Tests/PlayMode) - im
        /// normalen Spielbetrieb (Bootstrap.cs) bleibt der Parameter weg, dann kommt die echte
        /// UnityWebRequestAuthClient gegen Lumiis.Auth zum Einsatz.</summary>
        public void Initialize(NetClient netClient, GameClient gameClient, IAuthClient authClient = null, HudController hud = null)
        {
            _netClient = netClient;
            _gameClient = gameClient;
            _hud = hud;
            if (_hud != null) _hud.SystemMenuRequested += OnSystemMenuRequested;
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
            // Eigene Skalierung (Referenz 1280x720, Mindestfaktor 1, siehe UiSkin.ComputeScale) und Sortierung über dem HUD - Escape-Menü liegt vorn.
            _uiDocument.panelSettings = UiSkin.CreatePanelSettings(100f);
            go.AddComponent<UiScaler>().Initialize(_uiDocument.panelSettings);

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
            // Ohne das wären die geklonten TemplateContainer 0 px hoch (alle Kinder absolut positioniert) -
            // genau das hat den Login-Bildschirm halb aus dem Bild geschoben, siehe UiSkin.StretchToFill.
            UiSkin.StretchToFill(_loginRoot);
            UiSkin.StretchToFill(_characterSelectRoot);
            UiSkin.StretchToFill(_escapeMenuRoot);
            _uiDocument.rootVisualElement.pickingMode = PickingMode.Ignore;
            _uiDocument.rootVisualElement.Add(_loginRoot);
            _uiDocument.rootVisualElement.Add(_characterSelectRoot);
            ApplyBackdrop(_loginRoot, "login-backdrop");
            ApplyBackdrop(_characterSelectRoot, "character-select-backdrop");
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
            _startGameButton = _characterSelectRoot.Q<Button>("start-game-button");
            _previewFigure = _characterSelectRoot.Q<VisualElement>("charselect-preview-figure");
            _selectedNameLabel = _characterSelectRoot.Q<Label>("charselect-selected-name");
            _selectedInfoLabel = _characterSelectRoot.Q<Label>("charselect-selected-info");
            BindLookChoice();
            UiSkin.BuildCharacterPortrait(_previewFigure, 144f);

            _usernameField.value = PlayerPrefs.GetString(UsernamePrefsKey, "");

            _loginButton.clicked += OnLoginButtonClicked;
            _registerButton.clicked += OnRegisterButtonClicked;
            _createCharacterButton.clicked += OnCreateCharacterButtonClicked;
            _logoutButton.clicked += OnLogoutButtonClicked;
            _startGameButton.clicked += OnStartGameButtonClicked;
            _loginRoot.Q<Button>("login-quit-button").clicked += QuitGame;

            // Enter im Passwortfeld meldet an, Enter im Namensfeld springt ins Passwortfeld.
            _usernameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) _passwordField.Focus();
            }, TrickleDown.TrickleDown);
            _passwordField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && _loginButton.enabledSelf)
                    OnLoginButtonClicked();
            }, TrickleDown.TrickleDown);

            _escapeMenuRoot.Q<Button>("escape-resume-button").clicked += CloseEscapeMenu;
            _escapeMenuRoot.Q<Button>("escape-switch-character-button").clicked += SwitchCharacter;
            _escapeMenuRoot.Q<Button>("escape-logout-button").clicked += Logout;
            _escapeMenuRoot.Q<Button>("escape-quit-button").clicked += QuitGame;
        }

        /// <summary>Setzt das Map1-Stimmungsbild dezent hinter Login/Charakterauswahl. Fehlt das Asset,
        /// bleibt der dunkle Hintergrund aus dem USS erhalten.</summary>
        private static void ApplyBackdrop(VisualElement root, string elementName)
        {
            VisualElement backdrop = root.Q<VisualElement>(elementName);
            if (backdrop == null) return;

            Sprite map = Resources.Load<Sprite>("Maps/map1");
            if (map != null && map.texture != null)
                backdrop.style.backgroundImage = new StyleBackground(map.texture);
        }

        private void Update()
        {
            // Nur während eines laufenden Spiels erreichbar - Login/Charakterauswahl haben kein Escape-Menü.
            if (_loginFlow.State != LoginFlowState.InGame) return;
            if (Keyboard.current == null) return;
            if (_gameClient != null && (_gameClient.IsChatInputFocused || _gameClient.ChatClosedRecently)) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            // Escape schließt zuerst ein offenes Spielfenster (Inventar, Charakter, Shop), erst danach
            // öffnet es das Systemmenü.
            if (!_escapeMenuOpen && _hud != null && _hud.CloseTopWindow()) return;
            ToggleEscapeMenu();
        }

        private void OnSystemMenuRequested()
        {
            if (_loginFlow.State != LoginFlowState.InGame) return;
            ToggleEscapeMenu();
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

        /// <summary>Charaktere als Auswahlkarten: Klick wählt aus (Vorschau links), Doppelklick oder
        /// "Spiel starten" verbindet. Der erste Charakter ist vorausgewählt.</summary>
        private void PopulateCharacterList()
        {
            _characterListView.Clear();
            CharacterInfo? previous = _selectedCharacter;
            _selectedCharacter = null;
            foreach (CharacterInfo character in _loginFlow.Characters)
            {
                CharacterInfo captured = character; // Schleifenvariable für den Closure einfangen.
                var button = new Button(() => SelectCharacterCard(captured)) { text = captured.Name };
                button.AddToClassList("charslot");
                button.userData = captured;
                button.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.clickCount >= 2) StartWithCharacter(captured);
                });
                _characterListView.Add(button);
                if (previous.HasValue && previous.Value.Id == captured.Id) _selectedCharacter = captured;
            }

            if (_selectedCharacter == null && _loginFlow.Characters.Count > 0)
                _selectedCharacter = _loginFlow.Characters[0];
            RefreshCharacterSelection();
        }

        private void SelectCharacterCard(CharacterInfo character)
        {
            _selectedCharacter = character;
            _previewingNewLook = false;
            RefreshCharacterSelection();
        }

        // ---- Aussehen bei der Erstellung ----

        private void BindLookChoice()
        {
            try
            {
                _lookCatalog = LookCatalogClient.LoadFromStreamingAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("LoginController: looks.json nicht geladen, Aussehenswahl aus: " + ex.Message);
            }

            _lookBodyValue = _characterSelectRoot.Q<Label>("look-body-value");
            _lookHairValue = _characterSelectRoot.Q<Label>("look-hair-value");
            _lookColorSwatch = _characterSelectRoot.Q<VisualElement>("look-color-swatch");
            _characterSelectRoot.Q<Button>("look-body-prev").clicked += () => StepLook(ref _lookBodyIndex, LookIds("Body").Count, -1);
            _characterSelectRoot.Q<Button>("look-body-next").clicked += () => StepLook(ref _lookBodyIndex, LookIds("Body").Count, 1);
            _characterSelectRoot.Q<Button>("look-hair-prev").clicked += () => StepLook(ref _lookHairIndex, LookIds("Hair").Count, -1);
            _characterSelectRoot.Q<Button>("look-hair-next").clicked += () => StepLook(ref _lookHairIndex, LookIds("Hair").Count, 1);
            _characterSelectRoot.Q<Button>("look-color-prev").clicked += () => StepLook(ref _lookColorIndex, HairColorCount(), -1);
            _characterSelectRoot.Q<Button>("look-color-next").clicked += () => StepLook(ref _lookColorIndex, HairColorCount(), 1);
            // Nur ein Körper im Katalog: Zeile ausblenden statt eines toten Cyclers.
            _characterSelectRoot.Q<VisualElement>("look-body-row").style.display =
                LookIds("Body").Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _newCharacterNameField.RegisterCallback<FocusInEvent>(_ => ShowNewLookPreview());
            _newCharacterNameField.RegisterValueChangedCallback(evt =>
            {
                if (!_previewingNewLook) return;
                string typed = evt.newValue != null ? evt.newValue.Trim() : "";
                _selectedNameLabel.text = typed.Length > 0 ? typed : "Neuer Charakter";
            });
            RefreshLookLabels();
        }

        private System.Collections.Generic.IReadOnlyList<int> LookIds(string slot)
        {
            return _lookCatalog != null ? _lookCatalog.GetIdsForSlot(slot) : System.Array.Empty<int>();
        }

        private int HairColorCount() => _lookCatalog != null ? _lookCatalog.HairColors.Count : 0;

        private void StepLook(ref int index, int count, int delta)
        {
            if (count <= 0) return;
            index = ((index + delta) % count + count) % count;
            RefreshLookLabels();
            ShowNewLookPreview();
        }

        private void RefreshLookLabels()
        {
            _lookBodyValue.text = LookName("Body", _lookBodyIndex);
            _lookHairValue.text = LookName("Hair", _lookHairIndex);
            _lookColorSwatch.style.backgroundColor = HairColor(_lookColorIndex + 1);
        }

        private string LookName(string slot, int index)
        {
            System.Collections.Generic.IReadOnlyList<int> ids = LookIds(slot);
            if (index < 0 || index >= ids.Count) return "-";
            return _lookCatalog.TryGetEntry(slot, ids[index], out LookEntryJson entry) && entry.name.Length > 0
                ? entry.name
                : "#" + ids[index];
        }

        private Color HairColor(int hairColorId)
        {
            int index = hairColorId - 1; // 1-basiert wie Appearance.HairColorId
            if (_lookCatalog == null || index < 0 || index >= _lookCatalog.HairColors.Count) return Color.white;
            return ColorUtility.TryParseHtmlString(_lookCatalog.HairColors[index], out Color color) ? color : Color.white;
        }

        /// <summary>Gewähltes Aussehen für POST /characters; ohne Katalog bleibt es leer (Server-Default).</summary>
        private CharacterLookChoice CurrentLookChoice()
        {
            if (_lookCatalog == null) return default;
            System.Collections.Generic.IReadOnlyList<int> bodies = LookIds("Body");
            System.Collections.Generic.IReadOnlyList<int> hairs = LookIds("Hair");
            int? body = _lookBodyIndex < bodies.Count ? bodies[_lookBodyIndex] : (int?)null;
            int? hair = _lookHairIndex < hairs.Count ? hairs[_lookHairIndex] : (int?)null;
            int? color = _lookColorIndex < HairColorCount() ? _lookColorIndex + 1 : (int?)null;
            return new CharacterLookChoice(body, hair, color);
        }

        private void ShowNewLookPreview()
        {
            _previewingNewLook = true;
            RefreshCharacterSelection();
        }

        private void RefreshPreviewFigure()
        {
            Appearance appearance;
            if (_previewingNewLook)
            {
                CharacterLookChoice look = CurrentLookChoice();
                // Neue Charaktere bekommen serverseitig nur die Startwaffe (siehe Lumiis.Auth), keine Rüstung.
                appearance = new Appearance
                {
                    BodyId = look.BodyId ?? 1,
                    HairStyleId = look.HairStyleId ?? 1,
                    HairColorId = look.HairColorId ?? 0,
                    WeaponId = 1
                };
            }
            else if (_selectedCharacter.HasValue)
            {
                appearance = _selectedCharacter.Value.Appearance;
                if (appearance.BodyId <= 0) appearance.BodyId = 1; // alter Auth-Dienst ohne Aussehen in der Liste
            }
            else
            {
                return;
            }
            UiSkin.BuildCharacterPortrait(_previewFigure, 144f, appearance, HairColor(appearance.HairColorId));
        }

        private void RefreshCharacterSelection()
        {
            foreach (VisualElement card in _characterListView.Children())
            {
                bool selected = card.userData is CharacterInfo info && _selectedCharacter.HasValue && info.Id == _selectedCharacter.Value.Id;
                card.EnableInClassList("charslot-selected", selected);
            }

            bool hasSelection = _selectedCharacter.HasValue;
            if (_previewingNewLook)
            {
                string typed = _newCharacterNameField.value != null ? _newCharacterNameField.value.Trim() : "";
                _selectedNameLabel.text = typed.Length > 0 ? typed : "Neuer Charakter";
                _selectedInfoLabel.text = "Krieger · Stufe 1";
            }
            else
            {
                _selectedNameLabel.text = hasSelection ? _selectedCharacter.Value.Name : "Kein Charakter";
                _selectedInfoLabel.text = hasSelection ? "Krieger · Stufe " + Mathf.Max(1, _selectedCharacter.Value.Level) : "";
            }
            _previewFigure.style.visibility = hasSelection || _previewingNewLook ? Visibility.Visible : Visibility.Hidden;
            RefreshPreviewFigure();
            _startGameButton.SetEnabled(hasSelection && _characterListView.enabledSelf);
        }

        private void OnStartGameButtonClicked()
        {
            if (_selectedCharacter.HasValue) StartWithCharacter(_selectedCharacter.Value);
        }

        private void StartWithCharacter(CharacterInfo character)
        {
            if (_loginFlow.State != LoginFlowState.CharacterSelect) return;
            _selectedCharacter = character;
            _loginFlow.SelectCharacter(character);
            ApplyLoginFlowState();
        }

        private async void OnCreateCharacterButtonClicked()
        {
            SetCharacterSelectInteractable(false);
            SetCharacterSelectError("");
            _characterSelectStatusLabel.text = "Erstelle Charakter ...";
            await _loginFlow.CreateCharacterAsync(NormalizedAuthUrl(), _newCharacterNameField.value, CurrentLookChoice());
            _previewingNewLook = false;
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

        private void OnDestroy()
        {
            if (_hud != null) _hud.SystemMenuRequested -= OnSystemMenuRequested;
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
            if (_startGameButton != null) _startGameButton.SetEnabled(interactable && _selectedCharacter.HasValue);
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
