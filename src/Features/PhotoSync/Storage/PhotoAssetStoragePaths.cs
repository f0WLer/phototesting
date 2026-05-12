using Vintagestory.API.Config;

namespace Phototesting.PhotoSync.Storage
{
    // Photo id normalization and canonical on-disk photo path rules.
    internal static class PhotoAssetStoragePaths
    {
        internal static string NormalizePhotoId(string photoId)
        {
            if (string.IsNullOrWhiteSpace(photoId)) return string.Empty;

            // Keep only file name to prevent path traversal through packet-provided ids.
            string fileName = Path.GetFileName(photoId.Trim());
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            if (fileName == "." || fileName == "..") return string.Empty;
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return string.Empty;

            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            return fileName;
        }

        internal static string GetPhotoPath(string photoId)
        {
            string normalized = NormalizePhotoId(photoId);
            return Path.Combine(GamePaths.DataPath, "ModData", "phototesting", "photos", normalized);
        }
    }
}
