using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>Login/Registrierung/Charakterauswahl gegen Lumiis.Auth (HTTP), danach Verbindung zum
    /// Spielserver mit Token+CharacterId (M2b-Protokoll). Eigenes UIDocument (code-first erzeugt, kein
    /// Inspector-Verkabeln), schaltet zwischen Login.uxml und CharacterSelect.uxml um. Das bestehende
    /// Debug-Test-Panel (GameClient) bleibt unverändert und wird erst nach erfolgreichem Join sichtbar,
    /// weil GameClient seine eigene, von diesem Controller unabhängige InGame-Erkennung behält.</summary>
    public sealed class LoginController : MonoBehaviour
    {
        private enum State
        {
            LoggedOut,
            LoggingIn,
            CharacterSelect,
            Connecting,
            InGame
        }

        private const string UsernamePrefsKey = "Lumiis.LastUsername";

        // Clientseitige Vorprüfung, nur eine grobe Annäherung an die serverseitigen Regeln
        // (Lumiis.Data.UsernameRules/PasswordHasher - server-only, nicht nach Unity kopiert, siehe
        // CLAUDE.md-Architektur: Lumiis.Shared bleibt frei von Lumiis.Data). Verbindlich bleibt immer die
        // Server-Prüfung; Charakternamen dagegen nutzen direkt Lumiis.Shared.NameRules (echte Regel, geteilt).
        private const int MinUsernameLength = 3;
        private const int MaxUsernameLength = 32;
        private const int MinPasswordLength = 8;

        private NetClient _netClient;
        private GameClient _gameClient;

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

        private State _state = State.LoggedOut;
        private string _token;
        private List<CharacterDto> _characters = new List<CharacterDto>();

        public void Initialize(NetClient netClient, GameClient gameClient)
        {
            _netClient = netClient;
            _gameClient = gameClient;

            _netClient.PacketReceived += OnPacketReceived;
            _netClient.Disconnected += OnNetDisconnected;

            BuildUi();
            ShowLogin();
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
            _loginRoot = loginAsset.CloneTree();
            _characterSelectRoot = characterSelectAsset.CloneTree();
            _uiDocument.rootVisualElement.Add(_loginRoot);
            _uiDocument.rootVisualElement.Add(_characterSelectRoot);
            // Sofort auf den Anfangszustand (LoggedOut) einschränken, statt sich auf den direkt folgenden
            // ShowLogin()-Aufruf des Callers zu verlassen - BuildUi() soll für sich allein schon nie zwei
            // sichtbare Wurzeln gleichzeitig erzeugen.
            _characterSelectRoot.style.display = DisplayStyle.None;

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
        }

        private void ShowLogin()
        {
            _state = State.LoggedOut;
            _loginRoot.style.display = DisplayStyle.Flex;
            _characterSelectRoot.style.display = DisplayStyle.None;
            SetLoginInteractable(true);
            _loginStatusLabel.text = "";
            SetLoginError("");
        }

        private void ShowCharacterSelect()
        {
            _state = State.CharacterSelect;
            _loginRoot.style.display = DisplayStyle.None;
            _characterSelectRoot.style.display = DisplayStyle.Flex;
            SetCharacterSelectInteractable(true);
            _characterSelectStatusLabel.text = "";
            SetCharacterSelectError("");
        }

        private void HideAll()
        {
            _loginRoot.style.display = DisplayStyle.None;
            _characterSelectRoot.style.display = DisplayStyle.None;
        }

        // ---- Login/Registrierung ----

        private async void OnLoginButtonClicked()
        {
            string username = (_usernameField.value ?? "").Trim();
            string password = _passwordField.value ?? "";
            string validationError = ValidateCredentials(username, password);
            if (validationError != null)
            {
                SetLoginError(validationError);
                return;
            }

            _state = State.LoggingIn;
            SetLoginInteractable(false);
            SetLoginError("");
            _loginStatusLabel.text = "Melde an ...";

            try
            {
                await PerformLoginAsync(username, password);
            }
            catch (Exception ex)
            {
                Debug.LogError("Login fehlgeschlagen: " + ex);
                SetLoginError("Unerwarteter Fehler: " + ex.Message);
                ShowLogin();
            }
        }

        private async void OnRegisterButtonClicked()
        {
            string username = (_usernameField.value ?? "").Trim();
            string password = _passwordField.value ?? "";
            string validationError = ValidateCredentials(username, password);
            if (validationError != null)
            {
                SetLoginError(validationError);
                return;
            }

            _state = State.LoggingIn;
            SetLoginInteractable(false);
            SetLoginError("");
            _loginStatusLabel.text = "Registriere ...";

            try
            {
                string authUrl = NormalizedAuthUrl();
                (bool success, int statusCode, string body) = await PostJsonAsync(
                    authUrl + "register", JsonUtility.ToJson(new CredentialsDto(username, password)));

                if (!success)
                {
                    SetLoginError(DescribeNetworkFailure());
                    ShowLogin();
                    return;
                }

                if (statusCode == 201)
                {
                    _loginStatusLabel.text = "Registriert, melde an ...";
                    await PerformLoginAsync(username, password);
                    return;
                }

                SetLoginError(DescribeHttpError(statusCode, body,
                    conflictMessage: "Benutzername bereits vergeben.",
                    invalidMessage: "Ungültiger Benutzername oder Passwort."));
                ShowLogin();
            }
            catch (Exception ex)
            {
                Debug.LogError("Registrierung fehlgeschlagen: " + ex);
                SetLoginError("Unerwarteter Fehler: " + ex.Message);
                ShowLogin();
            }
        }

        private async Task PerformLoginAsync(string username, string password)
        {
            string authUrl = NormalizedAuthUrl();
            (bool success, int statusCode, string body) = await PostJsonAsync(
                authUrl + "login", JsonUtility.ToJson(new CredentialsDto(username, password)));

            if (!success)
            {
                SetLoginError(DescribeNetworkFailure());
                ShowLogin();
                return;
            }

            if (statusCode != 200)
            {
                SetLoginError(DescribeHttpError(statusCode, body));
                ShowLogin();
                return;
            }

            LoginResponseDto response = JsonUtility.FromJson<LoginResponseDto>(body);
            _token = response.token;

            PlayerPrefs.SetString(UsernamePrefsKey, username);
            PlayerPrefs.Save();

            ShowCharacterSelect();
            await RefreshCharacterListAsync();
        }

        // ---- Charakterauswahl ----

        private async Task RefreshCharacterListAsync()
        {
            _characterSelectStatusLabel.text = "Lade Charaktere ...";
            string authUrl = NormalizedAuthUrl();
            (bool success, int statusCode, string body) = await GetJsonAuthorizedAsync(authUrl + "characters");
            _characterSelectStatusLabel.text = "";

            if (!success)
            {
                SetCharacterSelectError(DescribeNetworkFailure());
                return;
            }

            if (statusCode == 401)
            {
                _token = null;
                ShowLogin();
                SetLoginError("Sitzung abgelaufen, bitte erneut anmelden.");
                return;
            }

            if (statusCode != 200)
            {
                SetCharacterSelectError(DescribeHttpError(statusCode, body));
                return;
            }

            CharacterListResponseDto listResponse = JsonUtility.FromJson<CharacterListResponseDto>(body);
            _characters = new List<CharacterDto>(listResponse.characters ?? Array.Empty<CharacterDto>());
            PopulateCharacterList();
        }

        private void PopulateCharacterList()
        {
            _characterListView.Clear();
            foreach (CharacterDto character in _characters)
            {
                CharacterDto captured = character; // Schleifenvariable für den Closure einfangen.
                var button = new Button(() => OnCharacterSelected(captured)) { text = captured.name };
                button.AddToClassList("character-button");
                _characterListView.Add(button);
            }
        }

        private void OnCharacterSelected(CharacterDto character)
        {
            ConnectToServer(character.id, character.name);
        }

        private async void OnCreateCharacterButtonClicked()
        {
            string name = (_newCharacterNameField.value ?? "").Trim();
            if (!NameRules.IsValid(name))
            {
                SetCharacterSelectError("Ungültiger Charaktername (" + NameRules.MinLength + "-" + NameRules.MaxLength
                    + " Zeichen, nur ASCII-Buchstaben/Ziffern/_).");
                return;
            }

            SetCharacterSelectInteractable(false);
            SetCharacterSelectError("");
            _characterSelectStatusLabel.text = "Erstelle Charakter ...";

            try
            {
                string authUrl = NormalizedAuthUrl();
                (bool success, int statusCode, string body) = await PostJsonAuthorizedAsync(
                    authUrl + "characters", JsonUtility.ToJson(new CreateCharacterRequestDto(name)));
                _characterSelectStatusLabel.text = "";

                if (!success)
                {
                    SetCharacterSelectError(DescribeNetworkFailure());
                    SetCharacterSelectInteractable(true);
                    return;
                }

                if (statusCode == 401)
                {
                    _token = null;
                    ShowLogin();
                    SetLoginError("Sitzung abgelaufen, bitte erneut anmelden.");
                    return;
                }

                if (statusCode != 201)
                {
                    SetCharacterSelectError(DescribeHttpError(statusCode, body,
                        conflictMessage: "Charaktername bereits vergeben.",
                        invalidMessage: "Ungültiger Charaktername."));
                    SetCharacterSelectInteractable(true);
                    return;
                }

                CharacterDto created = JsonUtility.FromJson<CharacterDto>(body);
                _newCharacterNameField.value = "";
                ConnectToServer(created.id, created.name);
            }
            catch (Exception ex)
            {
                Debug.LogError("Charaktererstellung fehlgeschlagen: " + ex);
                SetCharacterSelectError("Unerwarteter Fehler: " + ex.Message);
                SetCharacterSelectInteractable(true);
                _characterSelectStatusLabel.text = "";
            }
        }

        private void OnLogoutButtonClicked()
        {
            _token = null;
            _passwordField.value = "";
            ShowLogin();
        }

        // ---- Verbindung zum Spielserver ----

        private void ConnectToServer(int characterId, string characterName)
        {
            if (!int.TryParse(_serverPortField.value, out int port))
            {
                SetCharacterSelectError("Ungültiger Server-Port.");
                return;
            }

            _state = State.Connecting;
            SetCharacterSelectInteractable(false);
            SetCharacterSelectError("");
            _characterSelectStatusLabel.text = "Verbinde ...";

            _gameClient.PrepareJoin(characterName);
            bool started = _netClient.Connect(_serverHostField.value, port, _token, characterId);
            if (!started)
            {
                _state = State.CharacterSelect;
                SetCharacterSelectInteractable(true);
                _characterSelectStatusLabel.text = "";
                SetCharacterSelectError("Verbindung konnte nicht gestartet werden.");
            }
        }

        private void OnPacketReceived(IPacket packet)
        {
            if (packet is JoinAcceptedPacket)
            {
                _state = State.InGame;
                HideAll();
                return;
            }

            if (packet is JoinRejectedPacket rejected)
            {
                string message = DescribeJoinRejectReason(rejected.Reason);
                bool sessionInvalid = rejected.Reason == JoinRejectReason.InvalidToken || rejected.Reason == JoinRejectReason.TokenExpired;

                if (sessionInvalid)
                {
                    _token = null;
                    ShowLogin();
                    SetLoginError(message);
                }
                else
                {
                    ShowCharacterSelect();
                    SetCharacterSelectError(message);
                }
            }
        }

        private void OnNetDisconnected(string reason)
        {
            if (_state == State.InGame)
            {
                _token = null;
                _passwordField.value = "";
                ShowLogin();
                SetLoginError("Verbindung getrennt (" + reason + ").");
                return;
            }

            if (_state == State.Connecting)
            {
                // Ein JoinRejected wäre schon vorher über OnPacketReceived behandelt worden und hätte den
                // Zustand bereits auf CharacterSelect/LoggedOut zurückgesetzt - dieser Zweig läuft dann
                // nicht mehr. Hier landet nur der Fall "Verbindung selbst kam nie zustande" (Server nicht
                // erreichbar o. Ä.).
                ShowCharacterSelect();
                SetCharacterSelectError("Verbindung zum Server fehlgeschlagen (" + reason + ").");
            }

            // LoggedOut/LoggingIn/CharacterSelect: kein aktiver Verbindungsversuch betroffen, nichts zu tun.
        }

        // ---- Validierung ----

        private static string ValidateCredentials(string username, string password)
        {
            if (username.Length < MinUsernameLength || username.Length > MaxUsernameLength || !IsAsciiWordChars(username))
            {
                return "Benutzername muss " + MinUsernameLength + "-" + MaxUsernameLength
                    + " Zeichen lang sein (nur ASCII-Buchstaben/Ziffern/_).";
            }
            if (password.Length < MinPasswordLength)
                return "Passwort muss mindestens " + MinPasswordLength + " Zeichen lang sein.";
            return null;
        }

        private static bool IsAsciiWordChars(string value)
        {
            foreach (char c in value)
            {
                bool isAsciiLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool isAsciiDigit = c >= '0' && c <= '9';
                if (!isAsciiLetter && !isAsciiDigit && c != '_') return false;
            }
            return true;
        }

        // ---- Fehlertexte ----

        private static string DescribeNetworkFailure()
        {
            return "Lumiis.Auth nicht erreichbar. Läuft der Dienst? (siehe \"Erweitert\" -> Auth-URL)";
        }

        private static string DescribeHttpError(int statusCode, string body,
            string conflictMessage = "Bereits vergeben.", string invalidMessage = "Ungültige Eingabe.")
        {
            string serverMessage = TryGetErrorMessage(body);
            switch (statusCode)
            {
                case 400: return invalidMessage + (serverMessage != null ? " (" + serverMessage + ")" : "");
                case 401: return "Benutzername oder Passwort falsch.";
                case 409: return conflictMessage + (serverMessage != null ? " (" + serverMessage + ")" : "");
                default: return "Unerwarteter Fehler (" + statusCode + ")" + (serverMessage != null ? ": " + serverMessage : ".");
            }
        }

        private static string TryGetErrorMessage(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            try
            {
                ErrorResponseDto error = JsonUtility.FromJson<ErrorResponseDto>(body);
                return string.IsNullOrEmpty(error?.error) ? null : error.error;
            }
            catch
            {
                return null;
            }
        }

        private static string DescribeJoinRejectReason(JoinRejectReason reason)
        {
            switch (reason)
            {
                case JoinRejectReason.VersionMismatch: return "Protokollversion stimmt nicht überein - Client und Server sind nicht kompatibel.";
                case JoinRejectReason.InvalidToken: return "Sitzung ungültig, bitte erneut anmelden.";
                case JoinRejectReason.TokenExpired: return "Sitzung abgelaufen, bitte erneut anmelden.";
                case JoinRejectReason.CharacterNotFound: return "Charakter nicht gefunden.";
                case JoinRejectReason.CharacterAlreadyOnline: return "Dieser Charakter ist bereits online.";
                case JoinRejectReason.ServerFull: return "Server ist voll.";
                default: return reason.ToString();
            }
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

        // ---- HTTP (UnityWebRequest + TaskCompletionSource statt UnityEngine.Awaitable: SendWebRequest()
        // liefert eine UnityWebRequestAsyncOperation - ob deren Awaiter-Unterstützung in dieser Unity-
        // Version vollständig genug ist, um direkt await darauf zu nutzen, ließ sich ohne Play-Modus-Test
        // nicht verifizieren. TaskCompletionSource ist Standard-.NET, versionsunabhängig, und der
        // completed-Callback feuert ohnehin auf dem Hauptthread - unproblematisch für Folgezugriffe auf
        // Unity-APIs direkt danach.) ----

        private static Task<UnityWebRequest> SendAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            op.completed += _ => tcs.SetResult(request);
            return tcs.Task;
        }

        private async Task<(bool Success, int StatusCode, string Body)> PostJsonAsync(string url, string jsonBody, string bearerToken = null)
        {
            using var request = new UnityWebRequest(url, "POST");
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (bearerToken != null) request.SetRequestHeader("Authorization", "Bearer " + bearerToken);

            await SendAsync(request);
            return ToResult(request);
        }

        private Task<(bool Success, int StatusCode, string Body)> PostJsonAuthorizedAsync(string url, string jsonBody)
        {
            return PostJsonAsync(url, jsonBody, _token);
        }

        private async Task<(bool Success, int StatusCode, string Body)> GetJsonAuthorizedAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", "Bearer " + _token);

            await SendAsync(request);
            return ToResult(request);
        }

        private static (bool Success, int StatusCode, string Body) ToResult(UnityWebRequest request)
        {
            bool networkFailure = request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.DataProcessingError;
            return (!networkFailure, (int)request.responseCode, request.downloadHandler.text);
        }

        // ---- DTOs (JsonUtility: Feldnamen müssen exakt zur JSON-Antwort von Lumiis.Auth passen, siehe
        // dessen Dtos.cs - camelCase, da System.Text.Json das in ASP.NET Core standardmäßig so serialisiert). ----

        [Serializable]
        private sealed class CredentialsDto
        {
            public string username;
            public string password;

            public CredentialsDto(string username, string password)
            {
                this.username = username;
                this.password = password;
            }
        }

        [Serializable]
        private sealed class LoginResponseDto
        {
            public string token;
            public long expiresAtUnix;
            public CharacterDto[] characters;
        }

        [Serializable]
        private sealed class CharacterDto
        {
            public int id;
            public string name;
        }

        [Serializable]
        private sealed class CharacterListResponseDto
        {
            public CharacterDto[] characters;
        }

        [Serializable]
        private sealed class CreateCharacterRequestDto
        {
            public string name;

            public CreateCharacterRequestDto(string name)
            {
                this.name = name;
            }
        }

        [Serializable]
        private sealed class ErrorResponseDto
        {
            public string error;
        }
    }
}
