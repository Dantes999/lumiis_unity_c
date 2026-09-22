namespace Lumiis.Data;

/// <summary>Regeln für Account-Benutzernamen: eigenes, zu Lumiis.Shared.NameRules ähnlich strenges Pendant
/// (gleiches Zeichenset, größere Maximallänge) - Benutzernamen sind kein Wire-Protokoll-Feld wie
/// Charakternamen und unterliegen daher nicht denselben Längenvorgaben.</summary>
public static class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    public static bool IsValid(string? username)
    {
        if (username == null) return false;
        if (username.Length < MinLength || username.Length > MaxLength) return false;

        for (int i = 0; i < username.Length; i++)
        {
            char c = username[i];
            bool isAsciiLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            bool isAsciiDigit = c >= '0' && c <= '9';
            if (!isAsciiLetter && !isAsciiDigit && c != '_')
                return false;
        }

        return true;
    }
}
