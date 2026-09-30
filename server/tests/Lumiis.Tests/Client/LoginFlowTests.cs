using System.Collections.Generic;
using System.Threading.Tasks;
using Lumiis.Client;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Client
{
    /// <summary>Testet LoginFlow isoliert (kein Unity, kein HTTP) über eine steuerbare IAuthClient-Fake -
    /// verifiziert insbesondere Zustandsübergänge und Fehlertexte, die vorher nur durch manuelles Klicken im
    /// Play-Modus auffielen (siehe docs/tasks/client-test-infrastructure.md).</summary>
    public class LoginFlowTests
    {
        private const string AuthUrl = "http://127.0.0.1:5080/";
        private const string ValidUsername = "tester";
        private const string ValidPassword = "geheim123";

        [Fact]
        public async Task TryLoginAsync_UsernameTooShort_RejectsWithoutCallingAuthClient()
        {
            var fake = new FakeAuthClient();
            var flow = new LoginFlow(fake);

            await flow.TryLoginAsync(AuthUrl, "ab", ValidPassword);

            Assert.False(fake.LoginCalled);
            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.False(string.IsNullOrEmpty(flow.LoginErrorMessage));
        }

        [Fact]
        public async Task TryLoginAsync_PasswordTooShort_RejectsWithoutCallingAuthClient()
        {
            var fake = new FakeAuthClient();
            var flow = new LoginFlow(fake);

            await flow.TryLoginAsync(AuthUrl, ValidUsername, "kurz");

            Assert.False(fake.LoginCalled);
            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.False(string.IsNullOrEmpty(flow.LoginErrorMessage));
        }

        [Fact]
        public async Task TryRegisterAsync_InvalidUsername_RejectsWithoutCallingAuthClient()
        {
            var fake = new FakeAuthClient();
            var flow = new LoginFlow(fake);

            await flow.TryRegisterAsync(AuthUrl, "x", ValidPassword);

            Assert.False(fake.RegisterCalled);
            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.False(string.IsNullOrEmpty(flow.LoginErrorMessage));
        }

        [Fact]
        public async Task TryRegisterAsync_Success_AutoLoginTransitionsToCharacterSelect()
        {
            var fake = new FakeAuthClient
            {
                RegisterResult = AuthResult.Success(),
                LoginResult = AuthResult<string>.Success("tok-123"),
                GetCharactersResult = AuthResult<IReadOnlyList<CharacterInfo>>.Success(
                    new List<CharacterInfo> { new CharacterInfo(1, "Held") })
            };
            var flow = new LoginFlow(fake);

            await flow.TryRegisterAsync(AuthUrl, ValidUsername, ValidPassword);

            Assert.True(fake.RegisterCalled);
            Assert.True(fake.LoginCalled);
            Assert.True(fake.GetCharactersCalled);
            Assert.Equal(LoginFlowState.CharacterSelect, flow.State);
            Assert.Equal("tok-123", flow.Token);
            Assert.Single(flow.Characters);
            Assert.Equal("Held", flow.Characters[0].Name);
        }

        [Fact]
        public async Task TryLoginAsync_Success_TransitionsToCharacterSelectWithCharacters()
        {
            var fake = new FakeAuthClient
            {
                LoginResult = AuthResult<string>.Success("tok-abc"),
                GetCharactersResult = AuthResult<IReadOnlyList<CharacterInfo>>.Success(
                    new List<CharacterInfo> { new CharacterInfo(5, "Krieger") })
            };
            var flow = new LoginFlow(fake);

            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);

            Assert.Equal(LoginFlowState.CharacterSelect, flow.State);
            Assert.Equal("tok-abc", flow.Token);
            Assert.Equal("", flow.CharacterSelectErrorMessage);
            Assert.Single(flow.Characters);
        }

        [Fact]
        public async Task TryLoginAsync_WrongCredentials401_SetsErrorAndStaysLoggedOut()
        {
            var fake = new FakeAuthClient { LoginResult = AuthResult<string>.Http(401, null) };
            var flow = new LoginFlow(fake);

            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);

            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.Equal("Benutzername oder Passwort falsch.", flow.LoginErrorMessage);
            Assert.Null(flow.Token);
        }

        [Fact]
        public async Task TryRegisterAsync_UsernameTaken409_SetsConflictError()
        {
            var fake = new FakeAuthClient { RegisterResult = AuthResult.Http(409, "username taken") };
            var flow = new LoginFlow(fake);

            await flow.TryRegisterAsync(AuthUrl, ValidUsername, ValidPassword);

            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.Contains("Benutzername bereits vergeben.", flow.LoginErrorMessage);
            Assert.False(fake.LoginCalled); // Auto-Login läuft nur nach erfolgreicher Registrierung.
        }

        [Fact]
        public async Task TryLoginAsync_NetworkFailure_SetsNetworkErrorMessage()
        {
            var fake = new FakeAuthClient { LoginResult = AuthResult<string>.Network() };
            var flow = new LoginFlow(fake);

            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);

            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.Contains("nicht erreichbar", flow.LoginErrorMessage);
        }

        [Fact]
        public async Task TryLoginAsync_CharacterFetch401_ExpiresSessionBackToLoggedOut()
        {
            var fake = new FakeAuthClient
            {
                LoginResult = AuthResult<string>.Success("tok"),
                GetCharactersResult = AuthResult<IReadOnlyList<CharacterInfo>>.Http(401, null)
            };
            var flow = new LoginFlow(fake);

            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);

            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.Null(flow.Token);
            Assert.Equal("Sitzung abgelaufen, bitte erneut anmelden.", flow.LoginErrorMessage);
        }

        [Fact]
        public void SelectCharacter_SetsConnectingStateAndSelection()
        {
            var flow = new LoginFlow(new FakeAuthClient());

            flow.SelectCharacter(new CharacterInfo(7, "Waldläufer"));

            Assert.Equal(LoginFlowState.Connecting, flow.State);
            Assert.Equal(7, flow.SelectedCharacterId);
            Assert.Equal("Waldläufer", flow.SelectedCharacterName);
        }

        [Fact]
        public async Task CreateCharacterAsync_InvalidName_RejectsWithoutCallingAuthClient()
        {
            var fake = new FakeAuthClient();
            var flow = new LoginFlow(fake);

            await flow.CreateCharacterAsync(AuthUrl, "a");

            Assert.False(fake.CreateCharacterCalled);
            Assert.False(string.IsNullOrEmpty(flow.CharacterSelectErrorMessage));
        }

        [Fact]
        public async Task CreateCharacterAsync_Success_AddsCharacterAndTransitionsToConnecting()
        {
            var fake = new FakeAuthClient { CreateCharacterResult = AuthResult<CharacterInfo>.Success(new CharacterInfo(9, "Magier")) };
            var flow = new LoginFlow(fake);

            await flow.CreateCharacterAsync(AuthUrl, "Magier");

            Assert.Equal(LoginFlowState.Connecting, flow.State);
            Assert.Equal(9, flow.SelectedCharacterId);
            Assert.Equal("Magier", flow.SelectedCharacterName);
            Assert.Single(flow.Characters);
        }

        [Fact]
        public async Task CreateCharacterAsync_NameTaken409_SetsConflictError()
        {
            var fake = new FakeAuthClient { CreateCharacterResult = AuthResult<CharacterInfo>.Http(409, null) };
            var flow = new LoginFlow(fake);

            await flow.CreateCharacterAsync(AuthUrl, "Magier");

            Assert.Contains("Charaktername bereits vergeben.", flow.CharacterSelectErrorMessage);
        }

        [Fact]
        public async Task CreateCharacterAsync_401_ExpiresSession()
        {
            var fake = new FakeAuthClient { CreateCharacterResult = AuthResult<CharacterInfo>.Http(401, null) };
            var flow = new LoginFlow(fake);

            await flow.CreateCharacterAsync(AuthUrl, "Magier");

            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.Null(flow.Token);
            Assert.Equal("Sitzung abgelaufen, bitte erneut anmelden.", flow.LoginErrorMessage);
        }

        [Theory]
        [InlineData(JoinRejectReason.VersionMismatch, false)]
        [InlineData(JoinRejectReason.InvalidToken, true)]
        [InlineData(JoinRejectReason.TokenExpired, true)]
        [InlineData(JoinRejectReason.CharacterNotFound, false)]
        [InlineData(JoinRejectReason.CharacterAlreadyOnline, false)]
        [InlineData(JoinRejectReason.ServerFull, false)]
        public async Task OnJoinRejected_EachReason_TransitionsToExpectedState(JoinRejectReason reason, bool sessionInvalid)
        {
            var fake = new FakeAuthClient
            {
                LoginResult = AuthResult<string>.Success("tok"),
                GetCharactersResult = AuthResult<IReadOnlyList<CharacterInfo>>.Success(
                    new List<CharacterInfo> { new CharacterInfo(1, "Held") })
            };
            var flow = new LoginFlow(fake);
            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);
            flow.SelectCharacter(flow.Characters[0]);
            Assert.Equal(LoginFlowState.Connecting, flow.State);

            flow.OnJoinRejected(reason);

            if (sessionInvalid)
            {
                Assert.Equal(LoginFlowState.LoggedOut, flow.State);
                Assert.Null(flow.Token);
                Assert.False(string.IsNullOrEmpty(flow.LoginErrorMessage));
            }
            else
            {
                Assert.Equal(LoginFlowState.CharacterSelect, flow.State);
                Assert.False(string.IsNullOrEmpty(flow.CharacterSelectErrorMessage));
            }
        }

        [Fact]
        public void OnJoinAccepted_TransitionsToInGame()
        {
            var flow = new LoginFlow(new FakeAuthClient());
            flow.SelectCharacter(new CharacterInfo(1, "Held"));

            flow.OnJoinAccepted();

            Assert.Equal(LoginFlowState.InGame, flow.State);
        }

        [Fact]
        public async Task OnDisconnected_WhileInGame_ReturnsToLoggedOutWithMessage()
        {
            var fake = new FakeAuthClient { LoginResult = AuthResult<string>.Success("tok") };
            var flow = new LoginFlow(fake);
            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);
            flow.SelectCharacter(new CharacterInfo(1, "Held"));
            flow.OnJoinAccepted();
            Assert.Equal(LoginFlowState.InGame, flow.State);

            flow.OnDisconnected("Timeout");

            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.Null(flow.Token);
            Assert.Contains("Timeout", flow.LoginErrorMessage);
        }

        [Fact]
        public void OnDisconnected_WhileConnecting_ReturnsToCharacterSelectWithMessage()
        {
            var flow = new LoginFlow(new FakeAuthClient());
            flow.SelectCharacter(new CharacterInfo(1, "Held"));

            flow.OnDisconnected("Verbindung abgelehnt");

            Assert.Equal(LoginFlowState.CharacterSelect, flow.State);
            Assert.Contains("Verbindung abgelehnt", flow.CharacterSelectErrorMessage);
        }

        [Fact]
        public void CancelConnect_ReturnsToCharacterSelectWithMessage()
        {
            var flow = new LoginFlow(new FakeAuthClient());
            flow.SelectCharacter(new CharacterInfo(1, "Held"));

            flow.CancelConnect("Ungültiger Server-Port.");

            Assert.Equal(LoginFlowState.CharacterSelect, flow.State);
            Assert.Equal("Ungültiger Server-Port.", flow.CharacterSelectErrorMessage);
        }

        [Fact]
        public async Task Logout_ClearsTokenAndReturnsToLoggedOut()
        {
            var fake = new FakeAuthClient { LoginResult = AuthResult<string>.Success("tok") };
            var flow = new LoginFlow(fake);
            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);
            Assert.Equal("tok", flow.Token);

            flow.Logout();

            Assert.Equal(LoginFlowState.LoggedOut, flow.State);
            Assert.Null(flow.Token);
        }

        [Fact]
        public async Task SwitchCharacter_KeepsTokenAndReturnsToCharacterSelect()
        {
            var fake = new FakeAuthClient { LoginResult = AuthResult<string>.Success("tok") };
            var flow = new LoginFlow(fake);
            await flow.TryLoginAsync(AuthUrl, ValidUsername, ValidPassword);
            flow.SelectCharacter(new CharacterInfo(1, "Held"));
            flow.OnJoinAccepted();
            Assert.Equal(LoginFlowState.InGame, flow.State);

            flow.SwitchCharacter();

            Assert.Equal(LoginFlowState.CharacterSelect, flow.State);
            Assert.Equal("tok", flow.Token); // Token bleibt erhalten, anders als Logout.
        }
    }
}
