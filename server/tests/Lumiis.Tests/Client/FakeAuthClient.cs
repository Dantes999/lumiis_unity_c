using System.Collections.Generic;
using System.Threading.Tasks;
using Lumiis.Client;

namespace Lumiis.Tests.Client
{
    /// <summary>Steuerbare IAuthClient-Fake für LoginFlow-Tests - liefert vorab gesetzte Ergebnisse zurück und
    /// merkt sich, ob eine Methode aufgerufen wurde (z. B. um zu beweisen, dass eine clientseitige
    /// Validierung IAuthClient gar nicht erst kontaktiert).</summary>
    internal sealed class FakeAuthClient : IAuthClient
    {
        public bool RegisterCalled { get; private set; }
        public bool LoginCalled { get; private set; }
        public bool GetCharactersCalled { get; private set; }
        public bool CreateCharacterCalled { get; private set; }

        public AuthResult RegisterResult { get; set; } = AuthResult.Success();
        public AuthResult<string> LoginResult { get; set; } = AuthResult<string>.Success("token");
        public AuthResult<IReadOnlyList<CharacterInfo>> GetCharactersResult { get; set; } =
            AuthResult<IReadOnlyList<CharacterInfo>>.Success(System.Array.Empty<CharacterInfo>());
        public AuthResult<CharacterInfo> CreateCharacterResult { get; set; } =
            AuthResult<CharacterInfo>.Success(new CharacterInfo(1, "Held"));

        public Task<AuthResult> RegisterAsync(string authUrl, string username, string password)
        {
            RegisterCalled = true;
            return Task.FromResult(RegisterResult);
        }

        public Task<AuthResult<string>> LoginAsync(string authUrl, string username, string password)
        {
            LoginCalled = true;
            return Task.FromResult(LoginResult);
        }

        public Task<AuthResult<IReadOnlyList<CharacterInfo>>> GetCharactersAsync(string authUrl, string token)
        {
            GetCharactersCalled = true;
            return Task.FromResult(GetCharactersResult);
        }

        public CharacterLookChoice LastLookChoice { get; private set; }

        public Task<AuthResult<CharacterInfo>> CreateCharacterAsync(string authUrl, string token, string name, CharacterLookChoice look = default)
        {
            CreateCharacterCalled = true;
            LastLookChoice = look;
            return Task.FromResult(CreateCharacterResult);
        }
    }
}
