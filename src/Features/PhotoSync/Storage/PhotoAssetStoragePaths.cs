using SkiaSharp;
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

        // Generates a timestamped unique filename, encodes bitmap as PNG, writes it to the photo store, and returns the file name.
        internal static string SaveExposurePng(SKBitmap bitmap)
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string rnd = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            string fileName = $"exposure_{now}_{rnd}.png";
            string fullPath = GetPhotoPath(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var finalImage = SKImage.FromBitmap(bitmap);
            using var pngData = finalImage.Encode(SKEncodedImageFormat.Png, 90);
            using var output = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            pngData.SaveTo(output);
            return fileName;
        }
    }
}
