namespace TrameHub.Extensions
{
    public class TrameOptions
    {
        public bool EnableDetailedErrors { get; set; }

        public long? MaximumReceiveMessageSize { get; set; }

        public int? StreamBufferCapacity { get; set; }

        public int MaximumParallelInvocationsPerClient { get; set; }

        public bool UseMessagePack { get; set; }

        public bool UseSignalR { get; set; }

        /// <summary>
        /// Maximum number of RPC calls per client per second (0 = unlimited).
        /// </summary>
        public int RateLimitPermitLimit { get; set; } = 0;

        /// <summary>
        /// Time window for rate limiting in seconds.
        /// </summary>
        public int RateLimitWindowSeconds { get; set; } = 10;

        /// <summary>
        /// Whether to enforce authentication for all RPC calls.
        /// </summary>
        public bool RequireAuthentication { get; set; } = false;

        /// <summary>
        /// Maximale Elementzahl eines Array-/Collection-Parameters (Default 1000, 0 = unbegrenzt).
        /// Schützt vor Kardinalitäts-Sprengung beim @alias-Whole-Collection-Passthrough, wo der
        /// Server das Array aus einem früheren Ergebnis zur Laufzeit erzeugt — Body-Size-Limits
        /// greifen dort nicht, weil die Kardinalität serverseitig entsteht. Enforceiert im
        /// Invoker vor dem Methodenaufruf (Top-Level-Parameter; string/byte[] ausgenommen).
        /// </summary>
        public int MaxParameterArrayLength { get; set; } = 1000;

        /// <summary>
        /// Maximale Elementzahl eines Array-/Collection-Rückgabewerts (Default 10000, 0 = unbegrenzt).
        /// Verhindert, dass ein Einzelergebnis den Server in Memory treibt. Greift auf
        /// materialisierte Collections (List/Array/Dictionary) und IAsyncEnumerable-Streams
        /// (Early-Stop beim Konsumieren). Top-Level-Result; string/byte[] ausgenommen.
        /// </summary>
        public int MaxResultElementCount { get; set; } = 10000;

        /// <summary>
        /// Wie ein extrahiertes @alias-Fragment an den Consumer-Parametertyp gebunden wird
        /// (Default <see cref="TrameCommon.Models.AliasBindingMode.Weak"/>). Weak = STJ-
        /// Duck-Typing mit stillen Defaults (mächtig; Subset-Fan-out funktioniert).
        /// Strict = das Fragment muss den Consumer-Typ vollständig decken (jede public
        /// read-write Eigenschaft muss im Fragment vorhanden sein), sonst 400 — schaltet
        /// nur die object→object-silent-default-Zeile um; cross-kind ist in beiden Modi
        /// 400. Greift nur @alias-sourced Parameter. Siehe DEPENDENCY_BINDING.md.
        /// </summary>
        public AliasBindingMode AliasBindingMode { get; set; } = AliasBindingMode.Weak;
    }
}
