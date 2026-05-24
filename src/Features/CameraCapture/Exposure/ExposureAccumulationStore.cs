using Vintagestory.API.Config;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Persists and restores raw exposure accumulation blobs between game sessions.
    /// Files are keyed by exposure ID and stored under the mod's data folder so they
    /// survive server restarts, logouts, and game relaunches.
    /// Each file is a self-describing binary blob produced by <see cref="IExposureAccumulator.SerializeAccumulation"/>.
    /// </summary>
    internal static class ExposureAccumulationStore
    {
        private const string FolderName = "partialexposures";
        private const string Extension  = ".pex";

        /// <summary>Returns the absolute path to the <c>.pex</c> file for the given exposure ID.</summary>
        internal static string GetStorePath(string exposureId)
        {
            string safeId = Path.GetFileName(exposureId.Trim());
            return Path.Combine(GamePaths.DataPath, "ModData", "phototesting", FolderName, safeId + Extension);
        }

        /// <summary>Writes the serialized accumulation blob to disk, creating the containing folder if it does not exist.</summary>
        internal static void Save(string exposureId, byte[] data)
        {
            if (string.IsNullOrEmpty(exposureId) || data == null || data.Length == 0) return;

            string path = GetStorePath(exposureId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        /// <summary>Reads a previously saved blob. Returns <see langword="false"/> when no file exists for this ID.</summary>
        internal static bool TryLoad(string exposureId, out byte[]? data)
        {
            data = null;
            if (string.IsNullOrEmpty(exposureId)) return false;

            string path = GetStorePath(exposureId);
            if (!File.Exists(path)) return false;

            data = File.ReadAllBytes(path);
            return data.Length > 0;
        }

        /// <summary>Deletes the saved partial for this exposure. Called after successful development or on expiry.</summary>
        internal static void Delete(string exposureId)
        {
            if (string.IsNullOrEmpty(exposureId)) return;

            string path = GetStorePath(exposureId);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
