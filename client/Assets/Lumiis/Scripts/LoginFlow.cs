using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Client
{
    public enum LoginFlowState
    {
        LoggedOut,
        LoggingIn,
        CharacterSelect,
        Connecting,
        InGame
    }

    /// <summary>Zustand und Ablauf von Login/Registrierung/Charakterauswahl, unabhängig von Unity/UnityEngine
    /// (verifiziert per Compile-Include in Lumiis.Tests, siehe Lumiis.Tests.csproj) - Gegenstück zu ClientWorld
    /// auf der Weltseite. LoginController (MonoBehaviour) verdrahtet nur noch UI-Elemente gegen diese Klasse,
    /// jede Entscheidungslogik (Validierung, Fehlertexte, Zustandswechsel) lebt hier.
    ///
    /// Verbindung zum Spielserver (LiteNetLib/NetClient) ist bewusst NICHT Teil dieser Klasse - das ist kein
    /// HTTP-Aufruf gegen Lumiis.Auth, sondern reines Transport. SelectCharacter/CreateCharacterAsync wechseln
    /// nur in den Connecting-Zustand und stellen Token/SelectedCharacterId/-Name bereit; der Aufrufer (LoginController)
    /// ruft darauf tatsächlich NetClient.Connect auf und meldet das Ergebnis über OnJoinAccepted/OnJoinRejected/
    /// OnDisconnected zurück.</summary>
    public sealed class LoginFlow
    {
        private const int MinUsernameLength = 3;
        private const int MaxUsernameLength = 32;
        private const int MinPasswordLength = 8;

        private const string NetworkFailureMessage = "Lumiis.Auth nicht erreichbar. Läuft der Dienst? (siehe \"Erweitert\" -> Auth-URL)";

        private readonly IAuthClient _authClient;
        private readonly List<CharacterInfo> _characters = new List<CharacterInfo>();

        public LoginFlow(IAuthClient authClient)
        {
            _authClient = authClient;
        }

        public LoginFlowState State { get; private set; } = LoginFlowState.LoggedOut;
        public string LoginErrorMessage { get; private set; } = "";
        public string CharacterSelectErrorMessage { get; private set; } = "";
        public string Token { get; private set; }
        public IReadOnlyList<CharacterInfo> Characters => _characters;
        public int SelectedCharacterId { get; private set; }
        public string SelectedCharacterName { get; private set; }

        // ---- Login/Registrierung ----

        public async Task TryRegisterAsync(string authUrl, string username, string password)
        {
            username = (username ?? "").Trim();
            password = password ?? "";
            string validationError = ValidateCredentials(username, password);
            if (validationError != null)
            {
                LoginErrorMessage = validationError;
                return;
            }

            State = LoginFlowState.LoggingIn;
            LoginErrorMessage = "";

            try
            {
                AuthResult result = await _authClient.RegisterAsync(authUrl, username, password);

                if (result.Outcome == AuthOutcome.NetworkFailure)
                {
                    State = LoginFlowState.LoggedOut;
                    LoginErrorMessage = NetworkFailureMessage;
                    return;
                }

                if (result.Outcome == AuthOutcome.Success)
                {
                    await TryLoginAsync(authUrl, username, password);
                    return;
                }

                State = LoginFlowState.LoggedOut;
                LoginErrorMessage = DescribeHttpError(result.StatusCode, result.ServerMessage,
                    conflictMessage: "Benutzername bereits vergeben.",
                    invalidMessage: "Ungültiger Benutzername oder Passwort.");
            }
            catch (Exception ex)
            {
                State = LoginFlowState.LoggedOut;
                LoginErrorMessage = "Unerwarteter Fehler: " + ex.Message;
            }
        }

        public async Task TryLoginAsync(string authUrl, string username, string password)
        {
            username = (username ?? "").Trim();
            password = password ?? "";
            string validationError = ValidateCredentials(username, password);
            if (validationError != null)
            {
                LoginErrorMessage = validationError;
                return;
            }

            State = LoginFlowState.LoggingIn;
            LoginErrorMessage = "";

            try
            {
                AuthResult<string> result = await _authClient.LoginAsync(authUrl, username, password);

                if (result.Outcome == AuthOutcome.NetworkFailure)
                {
                    State = LoginFlowState.LoggedOut;
                    LoginErrorMessage = NetworkFailureMessage;
                    return;
                }

                if (result.Outcome != AuthOutcome.Success)
                {
                    State = LoginFlowState.LoggedOut;
                    LoginErrorMessage = DescribeHttpError(result.StatusCode, result.ServerMessage);
                    return;
                }

                Token = result.Value;
                State = LoginFlowState.CharacterSelect;
                CharacterSelectErrorMessage = "";
                await RefreshCharactersAsync(authUrl);
            }
            catch (Exception ex)
            {
                State = LoginFlowState.LoggedOut;
                LoginErrorMessage = "Unerwarteter Fehler: " + ex.Message;
            }
        }

        public async Task RefreshCharactersAsync(string authUrl)
        {
            try
            {
                AuthResult<IReadOnlyList<CharacterInfo>> result = await _authClient.GetCharactersAsync(authUrl, Token);

                if (result.Outcome == AuthOutcome.NetworkFailure)
                {
                    CharacterSelectErrorMessage = NetworkFailureMessage;
                    return;
                }

                if (result.Outcome == AuthOutcome.HttpError && result.StatusCode == 401)
                {
                    ExpireSession();
                    return;
                }

                if (result.Outcome != AuthOutcome.Success)
                {
                    CharacterSelectErrorMessage = DescribeHttpError(result.StatusCode, result.ServerMessage);
                    return;
                }

                _characters.Clear();
                if (result.Value != null) _characters.AddRange(result.Value);
            }
            catch (Exception ex)
            {
                CharacterSelectErrorMessage = "Unerwarteter Fehler: " + ex.Message;
            }
        }

        // ---- Charakterauswahl ----

        public void SelectCharacter(CharacterInfo character)
        {
            SelectedCharacterId = character.Id;
            SelectedCharacterName = character.Name;
            CharacterSelectErrorMessage = "";
            State = LoginFlowState.Connecting;
        }

        public async Task CreateCharacterAsync(string authUrl, string name, CharacterLookChoice look = default)
        {
            name = (name ?? "").Trim();
            if (!NameRules.IsValid(name))
            {
                CharacterSelectErrorMessage = "Ungültiger Charaktername (" + NameRules.MinLength + "-" + NameRules.MaxLength
                    + " Zeichen, nur ASCII-Buchstaben/Ziffern/_).";
                return;
            }

            CharacterSelectErrorMessage = "";

            try
            {
                AuthResult<CharacterInfo> result = await _authClient.CreateCharacterAsync(authUrl, Token, name, look);

                if (result.Outcome == AuthOutcome.NetworkFailure)
                {
                    CharacterSelectErrorMessage = NetworkFailureMessage;
                    return;
                }

                if (result.Outcome == AuthOutcome.HttpError && result.StatusCode == 401)
                {
                    ExpireSession();
                    return;
                }

                if (result.Outcome != AuthOutcome.Success)
                {
                    CharacterSelectErrorMessage = DescribeHttpError(result.StatusCode, result.ServerMessage,
                        conflictMessage: "Charaktername bereits vergeben.",
                        invalidMessage: "Ungültiger Charaktername.");
                    return;
                }

                _characters.Add(result.Value);
                SelectCharacter(result.Value);
            }
            catch (Exception ex)
            {
                CharacterSelectErrorMessage = "Unerwarteter Fehler: " + ex.Message;
            }
        }

        /// <summary>Bricht einen laufenden Verbindungsversuch ab (z. B. ungültiger Server-Port oder
        /// NetClient.Connect konnte nicht starten - beides reines Transport-/UI-Wissen, das der Aufrufer prüft,
        /// nicht LoginFlow) und kehrt mit einer Fehlermeldung zur Charakterauswahl zurück.</summary>
        public void CancelConnect(string errorMessage)
        {
            State = LoginFlowState.CharacterSelect;
            CharacterSelectErrorMessage = errorMessage;
        }

        // ---- Rückmeldungen vom Spielserver-Join (LiteNetLib, über NetClient) ----

        public void OnJoinAccepted()
        {
            State = LoginFlowState.InGame;
        }

        public void OnJoinRejected(JoinRejectReason reason)
        {
            string message = DescribeJoinRejectReason(reason);
            bool sessionInvalid = reason == JoinRejectReason.InvalidToken || reason == JoinRejectReason.TokenExpired;

            if (sessionInvalid)
            {
                Token = null;
                State = LoginFlowState.LoggedOut;
                LoginErrorMessage = message;
            }
            else
            {
                State = LoginFlowState.CharacterSelect;
                CharacterSelectErrorMessage = message;
            }
        }

        public void OnDisconnected(string reason)
        {
            if (State == LoginFlowState.InGame)
            {
                Token = null;
                State = LoginFlowState.LoggedOut;
                LoginErrorMessage = "Verbindung getrennt (" + reason + ").";
                return;
            }

            if (State == LoginFlowState.Connecting)
            {
                // Ein JoinRejected wäre schon vorher über OnJoinRejected behandelt worden und hätte den
                // Zustand bereits auf CharacterSelect/LoggedOut zurückgesetzt - dieser Zweig läuft dann nicht
                // mehr. Hier landet nur der Fall "Verbindung selbst kam nie zustande" (Server nicht erreichbar).
                State = LoginFlowState.CharacterSelect;
                CharacterSelectErrorMessage = "Verbindung zum Server fehlgeschlagen (" + reason + ").";
            }

            // LoggedOut/LoggingIn/CharacterSelect: kein aktiver Verbindungsversuch betroffen, nichts zu tun.
        }

        public void Logout()
        {
            Token = null;
            State = LoginFlowState.LoggedOut;
            LoginErrorMessage = "";
        }

        /// <summary>escape-menu "Charakter wechseln": wechselt NUR den Zustand, behält das Token (anders als
        /// Logout). Muss vom Aufrufer VOR dem tatsächlichen Trennen der Spielverbindung aufgerufen werden -
        /// OnDisconnected behandelt sonst jeden Disconnect aus InGame als unfreiwillig (Token verwerfen,
        /// State auf LoggedOut), das wäre hier falsch. Der Aufrufer (LoginController) ruft danach
        /// RefreshCharactersAsync auf, um die Liste neu zu laden - das behandelt ein zwischenzeitlich
        /// abgelaufenes Token bereits korrekt (401 -> ExpireSession, siehe dort), kein eigener Codepfad
        /// hier nötig.</summary>
        public void SwitchCharacter()
        {
            State = LoginFlowState.CharacterSelect;
            CharacterSelectErrorMessage = "";
        }

        private void ExpireSession()
        {
            Token = null;
            State = LoginFlowState.LoggedOut;
            LoginErrorMessage = "Sitzung abgelaufen, bitte erneut anmelden.";
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

        private static string DescribeHttpError(int statusCode, string serverMessage,
            string conflictMessage = "Bereits vergeben.", string invalidMessage = "Ungültige Eingabe.")
        {
            switch (statusCode)
            {
                case 400: return invalidMessage + (serverMessage != null ? " (" + serverMessage + ")" : "");
                case 401: return "Benutzername oder Passwort falsch.";
                case 409: return conflictMessage + (serverMessage != null ? " (" + serverMessage + ")" : "");
                default: return "Unerwarteter Fehler (" + statusCode + ")" + (serverMessage != null ? ": " + serverMessage : ".");
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
    }
}
