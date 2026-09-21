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
                if (!char.IsLetter(c) && !char.IsDigit(c) && c != '_')
                    return false;
            }

            return true;
        }
    }
}
