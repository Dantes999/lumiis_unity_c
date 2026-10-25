using System.Collections.Generic;
using System.Threading.Tasks;
using Lumiis.Shared.Net;

namespace Lumiis.Client
{
    // Bewusst ohne UnityEngine-Referenz (siehe LoginFlow.cs) - einfache Ergebnis-Typen statt UnityWebRequest/
    // JsonUtility, damit LoginFlow ohne Unity per dotnet test geprüft werden kann.

    public enum AuthOutcome
    {
        Success,
        HttpError,
        NetworkFailure
    }

    /// <summary>Ergebnis eines Aufrufs ohne Nutzdaten (z. B. Registrierung). ServerMessage ist die vom Server
    /// mitgelieferte Fehlermeldung (aus { "error": "..." }), falls vorhanden - sonst null.</summary>
    public readonly struct AuthResult
    {
        public AuthOutcome Outcome { get; }
        public int StatusCode { get; }
        public string ServerMessage { get; }

        private AuthResult(AuthOutcome outcome, int statusCode, string serverMessage)
        {
            Outcome = outcome;
            StatusCode = statusCode;
            ServerMessage = serverMessage;
        }

        public static AuthResult Success() => new AuthResult(AuthOutcome.Success, 0, null);
        public static AuthResult Http(int statusCode, string serverMessage) => new AuthResult(AuthOutcome.HttpError, statusCode, serverMessage);
        public static AuthResult Network() => new AuthResult(AuthOutcome.NetworkFailure, 0, null);
    }

    /// <summary>Wie AuthResult, aber mit Nutzdaten bei Erfolg (z. B. Token, Charakterliste).</summary>
    public readonly struct AuthResult<T>
    {
        public AuthOutcome Outcome { get; }
        public int StatusCode { get; }
        public string ServerMessage { get; }
        public T Value { get; }

        private AuthResult(AuthOutcome outcome, int statusCode, string serverMessage, T value)
        {
            Outcome = outcome;
            StatusCode = statusCode;
            ServerMessage = serverMessage;
            Value = value;
        }

        public static AuthResult<T> Success(T value) => new AuthResult<T>(AuthOutcome.Success, 0, null, value);
        public static AuthResult<T> Http(int statusCode, string serverMessage) => new AuthResult<T>(AuthOutcome.HttpError, statusCode, serverMessage, default);
        public static AuthResult<T> Network() => new AuthResult<T>(AuthOutcome.NetworkFailure, 0, null, default);
    }

    /// <summary>Eintrag der Charakterliste. Appearance enthält LookIds (wie EntitySpawn/JoinAccepted) für die
    /// Vorschau in der Charakterauswahl; WeaponItemId/WeaponUpgradeLevel erlauben dieselbe Item-Waffe
    /// (spriteKey aus items.json, +7/+9-Glanz) wie im Spiel. Alle Werte kommen vom Auth-Dienst.</summary>
    public readonly struct CharacterInfo
    {
        public int Id { get; }
        public string Name { get; }
        public int Level { get; }
        public string MapId { get; }
        public Appearance Appearance { get; }
        public int WeaponItemId { get; }
        public int WeaponUpgradeLevel { get; }

        public CharacterInfo(int id, string name)
            : this(id, name, 1, null, default, 0, 0)
        {
        }

        public CharacterInfo(int id, string name, int level, string mapId, Appearance appearance, int weaponItemId, int weaponUpgradeLevel)
        {
            Id = id;
            Name = name;
            Level = level;
            MapId = mapId;
            Appearance = appearance;
            WeaponItemId = weaponItemId;
            WeaponUpgradeLevel = weaponUpgradeLevel;
        }
    }

    /// <summary>Vertrag gegen Lumiis.Auth (HTTP), ohne UnityEngine-/UnityWebRequest-Typen im Signatur - so bleibt
    /// LoginFlow ohne Unity testbar. UnityWebRequestAuthClient ist die einzige echte Implementierung.</summary>
    public interface IAuthClient
    {
        Task<AuthResult> RegisterAsync(string authUrl, string username, string password);
        Task<AuthResult<string>> LoginAsync(string authUrl, string username, string password);
        Task<AuthResult<IReadOnlyList<CharacterInfo>>> GetCharactersAsync(string authUrl, string token);
        Task<AuthResult<CharacterInfo>> CreateCharacterAsync(string authUrl, string token, string name);
    }
}
