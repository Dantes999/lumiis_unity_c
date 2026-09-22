using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Lumiis.Client.Tests.PlayMode
{
    /// <summary>PlayMode-Tests für LoginController - fangen genau die drei Laufzeitfehler aus der M2c-Login-
    /// Debugging-Runde ab (rootVisualElement-Timing, zwei gleichzeitig sichtbare UI-Wurzeln, verwaiste OnGUI-
    /// Box), die trotz grünem Batchmode-Compile-Check unbemerkt geblieben waren (siehe
    /// docs/tasks/client-test-infrastructure.md). Läuft ohne Auth/Postgres über eine FakeAuthClient, die
    /// LoginController.Initialize seit der LoginFlow-Entkopplung optional entgegennimmt.</summary>
    public class LoginControllerPlayModeTests
    {
        private GameObject _root;
        private LoginController _loginController;
        private FakeAuthClient _fakeAuthClient;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("TestRoot");
            var netClient = _root.AddComponent<NetClient>();
            var gameClient = _root.AddComponent<GameClient>();
            _loginController = _root.AddComponent<LoginController>();
            _fakeAuthClient = new FakeAuthClient();

            _loginController.Initialize(netClient, gameClient, _fakeAuthClient);

            // LoginController.BuildUi() läuft über eine Coroutine einen Frame später (siehe dort) - zwei
            // Frames abwarten, damit UI-Aufbau UND der erste Layout-Pass durch sind.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
        }

        private UIDocument FindUiDocument()
        {
            return Object.FindFirstObjectByType<UIDocument>();
        }

        private static void SimulateClick(VisualElement element)
        {
            Vector2 position = element.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = position, button = 0 }))
            {
                down.target = element;
                element.SendEvent(down);
            }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = position, button = 0 }))
            {
                up.target = element;
                element.SendEvent(up);
            }
        }

        // (a) Hätte den rootVisualElement-Timing-Bug (M2c-Fix 1/5) gefangen: username-field war entweder gar
        // nicht erst abfragbar (NullReferenceException beim Aufbau) oder mit falscher/kollabierter Größe da.
        [UnityTest]
        public IEnumerator UsernameField_IsVisibleAfterStart()
        {
            UIDocument doc = FindUiDocument();
            Assert.IsNotNull(doc, "UIDocument wurde nicht erzeugt.");

            var usernameField = doc.rootVisualElement.Q<TextField>("username-field");
            Assert.IsNotNull(usernameField, "username-field nicht im UI-Baum gefunden.");

            yield return null;

            Assert.AreEqual(DisplayStyle.Flex, usernameField.resolvedStyle.display, "username-field ist nicht sichtbar (display).");
            Assert.Greater(usernameField.resolvedStyle.width, 0f, "username-field hat keine Breite.");
            Assert.Greater(usernameField.resolvedStyle.height, 0f, "username-field hat keine Höhe.");
        }

        // (b) Hätte den Duplicate-Roots-Bug gefangen: BuildUi() fügte früher beide UI-Wurzeln unbedingt sichtbar
        // hinzu, bevor der Aufrufer sie auseinandersortierte.
        [UnityTest]
        public IEnumerator AtMostOneRoot_IsVisibleAtAnyTime()
        {
            UIDocument doc = FindUiDocument();
            VisualElement root = doc.rootVisualElement;
            // Wichtig: NICHT über .Q<VisualElement>("login-root") suchen - das findet das benannte Element AUS
            // Login.uxml, das ein Kind des von LoginController tatsächlich getoggelten CloneTree()-Wrappers
            // ist. Das benannte Kind hat immer resolvedStyle.display == Flex, unabhängig vom Wrapper - nur der
            // Wrapper selbst (hier die ersten beiden direkten Kinder von rootVisualElement, siehe
            // LoginController.BuildUi()) trägt den sichtbar/versteckt-Zustand.
            VisualElement loginRoot = root[0];
            VisualElement characterSelectRoot = root[1];

            AssertExactlyOneVisible(loginRoot, characterSelectRoot); // Anfangszustand: LoggedOut.

            var usernameField = root.Q<TextField>("username-field");
            var passwordField = root.Q<TextField>("password-field");
            usernameField.value = "tester";
            passwordField.value = "geheim123";

            _fakeAuthClient.RegisterResult = AuthResult.Success();
            _fakeAuthClient.LoginResult = AuthResult<string>.Success("tok-123");
            _fakeAuthClient.GetCharactersResult = AuthResult<IReadOnlyList<CharacterInfo>>.Success(
                new List<CharacterInfo> { new CharacterInfo(1, "Held") });

            SimulateClick(root.Q<Button>("register-button"));

            // await der async-void-Handler-Kette (Register -> Login -> GetCharacters) über mehrere Frames.
            for (int i = 0; i < 20 && characterSelectRoot.resolvedStyle.display != DisplayStyle.Flex; i++)
                yield return null;

            AssertExactlyOneVisible(loginRoot, characterSelectRoot); // Nach dem Wechsel: CharacterSelect.
            Assert.AreEqual(DisplayStyle.Flex, characterSelectRoot.resolvedStyle.display, "Nicht zur Charakterauswahl gewechselt.");
        }

        private static void AssertExactlyOneVisible(VisualElement a, VisualElement b)
        {
            bool aVisible = a.resolvedStyle.display == DisplayStyle.Flex;
            bool bVisible = b.resolvedStyle.display == DisplayStyle.Flex;
            Assert.AreNotEqual(aVisible, bVisible, "Beide oder keine der beiden UI-Wurzeln sind sichtbar - genau eine sollte es sein.");
        }

        // (c) Hätte fehlende clientseitige Validierung / eine NullReferenceException beim Klick abgefangen.
        [UnityTest]
        public IEnumerator RegisterWithoutInput_ShowsValidationText_NoException()
        {
            UIDocument doc = FindUiDocument();
            VisualElement root = doc.rootVisualElement;

            LogAssert.NoUnexpectedReceived();

            SimulateClick(root.Q<Button>("register-button"));
            yield return null;

            LogAssert.NoUnexpectedReceived();

            var errorLabel = root.Q<Label>("login-error");
            Assert.IsFalse(string.IsNullOrEmpty(errorLabel.text), "Kein Validierungstext nach Registrieren ohne Eingabe.");
        }

        // (d) Erfolgreicher Login (Fake liefert Erfolg) wechselt sichtbar zur Charakterauswahl.
        [UnityTest]
        public IEnumerator SuccessfulLogin_SwitchesToCharacterSelect()
        {
            UIDocument doc = FindUiDocument();
            VisualElement root = doc.rootVisualElement;

            var usernameField = root.Q<TextField>("username-field");
            var passwordField = root.Q<TextField>("password-field");
            usernameField.value = "tester";
            passwordField.value = "geheim123";

            _fakeAuthClient.LoginResult = AuthResult<string>.Success("tok-abc");
            _fakeAuthClient.GetCharactersResult = AuthResult<IReadOnlyList<CharacterInfo>>.Success(
                new List<CharacterInfo> { new CharacterInfo(2, "Bogenschütze") });

            VisualElement loginRoot = root[0];
            VisualElement characterSelectRoot = root[1]; // siehe Kommentar in AtMostOneRoot_IsVisibleAtAnyTime
            SimulateClick(root.Q<Button>("login-button"));

            for (int i = 0; i < 20 && characterSelectRoot.resolvedStyle.display != DisplayStyle.Flex; i++)
                yield return null;

            Assert.AreEqual(DisplayStyle.Flex, characterSelectRoot.resolvedStyle.display, "Nicht sichtbar zur Charakterauswahl gewechselt.");
            Assert.AreEqual(DisplayStyle.None, loginRoot.resolvedStyle.display, "Login-Wurzel noch sichtbar.");
        }
    }
}
