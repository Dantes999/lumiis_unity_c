using System.Security.Cryptography;

namespace Lumiis.Data;

/// <summary>PBKDF2-HMAC-SHA256 über Rfc2898DeriveBytes.Pbkdf2 (kein zusätzliches NuGet-Paket nötig).
/// Iterationszahl wird pro Account-Zeile gespeichert, damit eine spätere Erhöhung ohne Breaking Change
/// möglich ist (neue Hashes mit mehr Iterationen, alte Zeilen bleiben mit ihrer eigenen Zahl verifizierbar).</summary>
public static class PasswordHasher
{
    public const int SaltSize = 16;
    public const int HashSize = 32;

    /// <summary>Aktuelle OWASP-Mindestempfehlung für PBKDF2-HMAC-SHA256 (Stand M2a).</summary>
    public const int CurrentIterations = 600_000;

    public const int MinPasswordLength = 8;

    /// <summary>Keine weiteren Komplexitätsregeln - aktuelle Empfehlungen bevorzugen Länge vor
    /// Komplexitätstheater.</summary>
    public static bool IsValidPassword(string? password)
    {
        return password != null && password.Length >= MinPasswordLength;
    }

    public static (byte[] Hash, byte[] Salt, int Iterations) Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, CurrentIterations, HashAlgorithmName.SHA256, HashSize);
        return (hash, salt, CurrentIterations);
    }

    /// <summary>Konstantzeit-Vergleich (CryptographicOperations.FixedTimeEquals) - sicher auch bei
    /// unterschiedlich langen Hashes (liefert dann false, ohne den Inhalt zu vergleichen oder zu werfen).</summary>
    public static bool Verify(string password, byte[] expectedHash, byte[] salt, int iterations)
    {
        byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }
}
