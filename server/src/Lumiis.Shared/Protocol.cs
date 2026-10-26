namespace Lumiis.Shared
{
    public static class Protocol
    {
        /// <summary>Wird bei jeder inkompatiblen Protokolländerung erhöht.</summary>
        public const int Version = 24;

        /// <summary>Server-Ticks pro Sekunde.</summary>
        public const int TickRate = 25;

        /// <summary>LiteNetLib-Verbindungsschlüssel, den Server, Bot und Unity-Client nutzen.</summary>
        public const string ConnectionKey = "lumiis";

        /// <summary>Maximale Länge eines Strings im Paket (UTF-8-Bytes).</summary>
        public const int MaxStringBytes = 1024;

        /// <summary>Maximale Größe eines gesamten Pakets in Bytes; größere Pakete werden vor dem Decode verworfen.</summary>
        public const int MaxPacketBytes = 1200;
    }
}
