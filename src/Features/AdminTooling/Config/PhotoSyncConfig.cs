namespace Phototesting.AdminTooling
{
    public sealed class PhotoSyncConfig
    {
        /// <summary>Per-packet payload size for image sync. Lower = smaller packet bursts but more packets; higher = fewer packets but larger bursts.</summary>
        public int ChunkSizeBytes = 24 * 1024;

        /// <summary>Maximum allowed image transfer size for upload/download.</summary>
        public int MaxTransferBytes = 2 * 1024 * 1024;

        /// <summary>How often client prunes request/download bookkeeping state.</summary>
        public int ClientStateCleanupIntervalMs = 15_000;

        /// <summary>How long client request-dedupe entries are retained.</summary>
        public float ClientRequestRetainSeconds = 300f;

        /// <summary>Client timeout for incomplete incoming image assemblies.</summary>
        public int ClientIncomingStaleMs = 120_000;

        /// <summary>How often server checks in-progress uploads for stale assemblies.</summary>
        public int ServerPruneIntervalMs = 30_000;

        /// <summary>Server timeout for incomplete upload assemblies.</summary>
        public int ServerUploadStaleMs = 120_000;

        /// <summary>Maximum concurrent in-flight uploads accepted from one player. Excess uploads are dropped.</summary>
        public int ServerMaxOpenUploadsPerPlayer = 2;

        internal void ClampInPlace()
        {
            if (ChunkSizeBytes < 1024) ChunkSizeBytes = 1024;
            if (ChunkSizeBytes > 256 * 1024) ChunkSizeBytes = 256 * 1024;

            if (MaxTransferBytes < 16 * 1024) MaxTransferBytes = 16 * 1024;
            if (MaxTransferBytes > 32 * 1024 * 1024) MaxTransferBytes = 32 * 1024 * 1024;

            if (ClientStateCleanupIntervalMs < 250) ClientStateCleanupIntervalMs = 250;
            if (ClientStateCleanupIntervalMs > 10 * 60 * 1000) ClientStateCleanupIntervalMs = 10 * 60 * 1000;

            if (ClientRequestRetainSeconds < 0f) ClientRequestRetainSeconds = 0f;
            if (ClientRequestRetainSeconds > 24f * 60f * 60f) ClientRequestRetainSeconds = 24f * 60f * 60f;

            if (ClientIncomingStaleMs < 1000) ClientIncomingStaleMs = 1000;
            if (ClientIncomingStaleMs > 30 * 60 * 1000) ClientIncomingStaleMs = 30 * 60 * 1000;

            if (ServerPruneIntervalMs < 250) ServerPruneIntervalMs = 250;
            if (ServerPruneIntervalMs > 10 * 60 * 1000) ServerPruneIntervalMs = 10 * 60 * 1000;

            if (ServerUploadStaleMs < 1000) ServerUploadStaleMs = 1000;
            if (ServerUploadStaleMs > 30 * 60 * 1000) ServerUploadStaleMs = 30 * 60 * 1000;

            if (ServerMaxOpenUploadsPerPlayer < 1) ServerMaxOpenUploadsPerPlayer = 1;
            if (ServerMaxOpenUploadsPerPlayer > 32) ServerMaxOpenUploadsPerPlayer = 32;
        }
    }
}
