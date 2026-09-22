using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumiis.Data;

/// <summary>Signierte, zustandslose Session-Token: base64url(JSON-Payload) + "." + base64url(HMACSHA256-
/// Signatur über den Payload-Teil). Der Server (M2b) validiert das Token lokal per Signatur, kein
/// Netzwerk-Aufruf zu Lumiis.Auth nötig. Trägt nur die AccountId (keine CharacterId), damit ein Account
/// zwischen Charakteren wählen kann, ohne sich neu einzuloggen.</summary>
public sealed class TokenService
{
    public const int ValiditySeconds = 30 * 60;
    private const int MinSecretBytes = 32;

    private readonly byte[] _secret;
    private readonly Func<long> _nowUnixSeconds;

    /// <summary>secret: Signier-Geheimnis (siehe LUMIIS_TOKEN_SECRET), mindestens 32 Byte UTF8 - wirft, wenn
    /// nicht (bewusster Startabbruch bei Fehlkonfiguration, siehe .env.example). nowUnixSeconds: injizierbare
    /// Uhr für deterministische Tests, Default UTC-Systemzeit.</summary>
    public TokenService(string secret, Func<long>? nowUnixSeconds = null)
    {
        if (string.IsNullOrEmpty(secret) || Encoding.UTF8.GetByteCount(secret) < MinSecretBytes)
            throw new ArgumentException($"LUMIIS_TOKEN_SECRET muss mindestens {MinSecretBytes} Byte lang sein.", nameof(secret));

        _secret = Encoding.UTF8.GetBytes(secret);
        _nowUnixSeconds = nowUnixSeconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public string Issue(int accountId) => IssueWithExpiry(accountId).Token;

    /// <summary>Wie Issue, gibt zusätzlich den Ablaufzeitpunkt zurück - für Lumiis.Auth (POST /login), damit
    /// die Antwort exakt denselben Wert nennt, der auch im Token steckt (eine Uhrzeitquelle statt zwei
    /// unabhängiger DateTimeOffset.UtcNow-Aufrufe, die theoretisch auseinanderlaufen könnten).</summary>
    public (string Token, long ExpiresAtUnix) IssueWithExpiry(int accountId)
    {
        long expiresAtUnix = _nowUnixSeconds() + ValiditySeconds;
        var payload = new TokenPayload { Aid = accountId, Exp = expiresAtUnix };
        string payloadPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return (payloadPart + "." + ComputeSignature(payloadPart), expiresAtUnix);
    }

    /// <summary>Lehnt manipulierte Signatur, abgelaufenes Token und Kauderwelsch ab - wirft nie, egal was
    /// hereinkommt (das Token kommt später vom Client, siehe M2b).</summary>
    public bool TryValidate(string? token, out int accountId)
    {
        accountId = 0;
        if (string.IsNullOrEmpty(token)) return false;

        int dotIndex = token.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == token.Length - 1) return false;

        string payloadPart = token[..dotIndex];
        string signaturePart = token[(dotIndex + 1)..];

        try
        {
            if (!FixedTimeEqualsUtf8(signaturePart, ComputeSignature(payloadPart))) return false;

            TokenPayload? payload = JsonSerializer.Deserialize<TokenPayload>(Base64UrlDecode(payloadPart));
            if (payload == null) return false;
            if (payload.Exp < _nowUnixSeconds()) return false;

            accountId = payload.Aid;
            return true;
        }
        catch (Exception)
        {
            // "Wirft nie" gilt für beliebige Eingaben (das Token kommt vom Client, siehe M2b) - bewusst
            // breit gefangen statt einzelne Exception-Typen (FormatException, JsonException, ...)
            // aufzuzählen, da hier Korrektheit gegenüber Kauderwelsch wichtiger ist als Präzision.
            return false;
        }
    }

    private string ComputeSignature(string payloadPart)
    {
        byte[] signature = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(payloadPart));
        return Base64UrlEncode(signature);
    }

    private static bool FixedTimeEqualsUtf8(string a, string b)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string text)
    {
        string base64 = text.Replace('-', '+').Replace('_', '/');
        int padding = (4 - base64.Length % 4) % 4;
        if (padding == 3) throw new FormatException("Ungültige Base64Url-Länge.");
        return Convert.FromBase64String(base64 + new string('=', padding));
    }

    private sealed class TokenPayload
    {
        [JsonPropertyName("aid")]
        public int Aid { get; set; }

        [JsonPropertyName("exp")]
        public long Exp { get; set; }
    }
}
