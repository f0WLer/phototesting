using Vintagestory.API.Config;

namespace Phototesting.CameraCapture.Exposure
{
    // Persists and restores raw exposure accumulation blobs between game sessions.
    // Files are keyed by ExposureId and stored under the mod's data folder so they
    // survive server restarts, log-outs, and game relaunches.
    // Each file is a self-describing binary blob produced by IExposureAccumulator.SerializeAccumulation.
    internal static class ExposureAccumulationStore
    {
        private const string FolderName = "partialexposures";
        private const string Extension  = ".pex";

        internal static string GetStorePath(string exposureId)
        {
            string safeId = Path.GetFileName(exposureId.Trim());
            return Path.Combine(GamePaths.DataPath, "ModData", "phototesting", FolderName, safeId + Extension);
        }

        // Writes the serialized accumulation blob to disk, creating the folder if needed.
        internal static void Save(string exposureId, byte[] data)
        {
            if (string.IsNullOrEmpty(exposureId) || data == null || data.Length == 0) return;

            string path = GetStorePath(exposureId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        // Reads a previously saved blob. Returns false when no file exists for this id.
        internal static bool TryLoad(string exposureId, out byte[]? data)
        {
            data = null;
            if (string.IsNullOrEmpty(exposureId)) return false;

            string path = GetStorePath(exposureId);
            if (!File.Exists(path)) return false;

            data = File.ReadAllBytes(path);
            return data.Length > 0;
        }

        // Removes the saved partial for this exposure (called after development or expiry).
        internal static void Delete(string exposureId)
        {
            if (string.IsNullOrEmpty(exposureId)) return;

            string path = GetStorePath(exposureId);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
