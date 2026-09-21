namespace Lumiis.Shared
{
    /// <summary>Regeln für Spielernamen: reine Funktion, ohne Netzwerk- oder Zustandsbezug.</summary>
    public static class NameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        public static bool IsValid(string name)
        {
            if (name == null) return false;
            if (name.Length < MinLength || name.Length > MaxLength) return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool isAsciiLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool isAsciiDigit = c >= '0' && c <= '9';
                if (!isAsciiLetter && !isAsciiDigit && c != '_')
                    return false;
            }

            return true;
        }
    }
}
