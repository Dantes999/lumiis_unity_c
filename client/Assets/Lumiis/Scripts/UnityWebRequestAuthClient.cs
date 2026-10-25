using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Lumiis.Shared.Net;

namespace Lumiis.Client
{
    /// <summary>Einzige IAuthClient-Implementierung, die tatsächlich HTTP spricht (UnityWebRequest) - bleibt
    /// bewusst dünn: nur Request bauen/schicken und Antwort in die UnityEngine-freien Ergebnis-Typen aus
    /// IAuthClient.cs übersetzen. Jede Entscheidungslogik (Validierung, Fehlertexte, Zustandswechsel) lebt in
    /// LoginFlow, nicht hier.</summary>
    public sealed class UnityWebRequestAuthClient : IAuthClient
    {
        public async Task<AuthResult> RegisterAsync(string authUrl, string username, string password)
        {
            (bool ok, int status, string body) = await SendJsonAsync(authUrl + "register", "POST",
                JsonUtility.ToJson(new CredentialsDto(username, password)));
            if (!ok) return AuthResult.Network();
            if (status == 201) return AuthResult.Success();
            return AuthResult.Http(status, TryGetServerMessage(body));
        }

        public async Task<AuthResult<string>> LoginAsync(string authUrl, string username, string password)
        {
            (bool ok, int status, string body) = await SendJsonAsync(authUrl + "login", "POST",
                JsonUtility.ToJson(new CredentialsDto(username, password)));
            if (!ok) return AuthResult<string>.Network();
            if (status != 200) return AuthResult<string>.Http(status, TryGetServerMessage(body));

            LoginResponseDto response = JsonUtility.FromJson<LoginResponseDto>(body);
            return AuthResult<string>.Success(response.token);
        }

        public async Task<AuthResult<IReadOnlyList<CharacterInfo>>> GetCharactersAsync(string authUrl, string token)
        {
            (bool ok, int status, string body) = await SendJsonAsync(authUrl + "characters", "GET", null, token);
            if (!ok) return AuthResult<IReadOnlyList<CharacterInfo>>.Network();
            if (status != 200) return AuthResult<IReadOnlyList<CharacterInfo>>.Http(status, TryGetServerMessage(body));

            CharacterListResponseDto listResponse = JsonUtility.FromJson<CharacterListResponseDto>(body);
            var characters = new List<CharacterInfo>();
            foreach (CharacterDto dto in listResponse.characters ?? Array.Empty<CharacterDto>())
                characters.Add(ToInfo(dto));
            return AuthResult<IReadOnlyList<CharacterInfo>>.Success(characters);
        }

        public async Task<AuthResult<CharacterInfo>> CreateCharacterAsync(string authUrl, string token, string name)
        {
            (bool ok, int status, string body) = await SendJsonAsync(authUrl + "characters", "POST",
                JsonUtility.ToJson(new CreateCharacterRequestDto(name)), token);
            if (!ok) return AuthResult<CharacterInfo>.Network();
            if (status != 201) return AuthResult<CharacterInfo>.Http(status, TryGetServerMessage(body));

            CharacterDto dto = JsonUtility.FromJson<CharacterDto>(body);
            return AuthResult<CharacterInfo>.Success(ToInfo(dto));
        }

        private static CharacterInfo ToInfo(CharacterDto dto)
        {
            AppearanceDto a = dto.appearance;
            Appearance appearance = a == null ? default : new Appearance
            {
                BodyId = a.bodyId,
                HairStyleId = a.hairStyleId,
                HairColorId = a.hairColorId,
                ArmorId = a.armorId,
                HelmetId = a.helmetId,
                WeaponId = a.weaponId,
                ShieldId = a.shieldId
            };
            EquipmentDto e = dto.equipment;
            return new CharacterInfo(dto.id, dto.name, dto.level <= 0 ? 1 : dto.level, dto.mapId, appearance,
                e?.weaponItemId ?? 0, e?.weaponUpgradeLevel ?? 0);
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

        private static async Task<(bool Success, int StatusCode, string Body)> SendJsonAsync(
            string url, string method, string jsonBody, string bearerToken = null)
        {
            using UnityWebRequest request = method == "GET" ? UnityWebRequest.Get(url) : new UnityWebRequest(url, method);
            if (jsonBody != null)
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            if (bearerToken != null) request.SetRequestHeader("Authorization", "Bearer " + bearerToken);

            await SendAsync(request);

            bool networkFailure = request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.DataProcessingError;
            return (!networkFailure, (int)request.responseCode, request.downloadHandler.text);
        }

        private static string TryGetServerMessage(string body)
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

        // ---- DTOs (JsonUtility: Feldnamen müssen exakt zur JSON-Antwort von Lumiis.Auth passen, siehe dessen
        // Dtos.cs - camelCase, da System.Text.Json das in ASP.NET Core standardmäßig so serialisiert). ----

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
            public int level;
            public string mapId;
            public AppearanceDto appearance;
            public EquipmentDto equipment;
        }

        [Serializable]
        private sealed class AppearanceDto
        {
            public int bodyId;
            public int hairStyleId;
            public int hairColorId;
            public int armorId;
            public int helmetId;
            public int weaponId;
            public int shieldId;
        }

        [Serializable]
        private sealed class EquipmentDto
        {
            public int armorItemId;
            public int helmetItemId;
            public int weaponItemId;
            public int shieldItemId;
            public int armorUpgradeLevel;
            public int helmetUpgradeLevel;
            public int weaponUpgradeLevel;
            public int shieldUpgradeLevel;
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
