namespace Lumiis.Shared
{
    public static class Protocol
    {
        /// <summary>Wird bei jeder inkompatiblen Protokolländerung erhöht.</summary>
        public const int Version = 1;

        /// <summary>Server-Ticks pro Sekunde.</summary>
        public const int TickRate = 25;

        /// <summary>Maximale Länge eines Strings im Paket (UTF-8-Bytes).</summary>
        public const int MaxStringBytes = 1024;
    }
}
