using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lumiis.Client.Tests.PlayMode
{
    /// <summary>Steuerbare IAuthClient-Fake für PlayMode-Tests - Gegenstück zu
    /// server/tests/Lumiis.Tests/Client/FakeAuthClient.cs (dort für xUnit, hier für Unity; getrennte
    /// Assemblies, deshalb dupliziert statt geteilt).</summary>
    internal sealed class FakeAuthClient : IAuthClient
    {
        public AuthResult RegisterResult { get; set; } = AuthResult.Success();
        public AuthResult<string> LoginResult { get; set; } = AuthResult<string>.Success("token");
        public AuthResult<IReadOnlyList<CharacterInfo>> GetCharactersResult { get; set; } =
            AuthResult<IReadOnlyList<CharacterInfo>>.Success(System.Array.Empty<CharacterInfo>());
        public AuthResult<CharacterInfo> CreateCharacterResult { get; set; } =
            AuthResult<CharacterInfo>.Success(new CharacterInfo(1, "Held"));

        public Task<AuthResult> RegisterAsync(string authUrl, string username, string password) => Task.FromResult(RegisterResult);
        public Task<AuthResult<string>> LoginAsync(string authUrl, string username, string password) => Task.FromResult(LoginResult);
        public Task<AuthResult<IReadOnlyList<CharacterInfo>>> GetCharactersAsync(string authUrl, string token) => Task.FromResult(GetCharactersResult);
        public Task<AuthResult<CharacterInfo>> CreateCharacterAsync(string authUrl, string token, string name) => Task.FromResult(CreateCharacterResult);
    }
}
