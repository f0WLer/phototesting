using Phototesting.PhotoSync.Storage;

namespace Phototesting.PhotoMetadata.Runtime
{
    // Central metadata policy seam for caption and photo-id normalization semantics.
    internal static class PhotoMetadataPolicy
    {
        internal static string NormalizePhotoId(string? photoId)
        {
            return PhotoAssetStoragePaths.NormalizePhotoId(photoId ?? string.Empty);
        }

        internal static string NormalizeCaptionForPacket(string? caption, int maxLength)
        {
            string normalized = caption ?? string.Empty;

            if (maxLength >= 0 && normalized.Length > maxLength)
            {
                normalized = normalized.Substring(0, maxLength);
            }

            return normalized;
        }

        internal static string? NormalizeCaptionOrNull(string? caption, int maxLength)
        {
            string? normalized = string.IsNullOrWhiteSpace(caption) ? null : caption;

            if (maxLength >= 0 && normalized != null && normalized.Length > maxLength)
            {
                normalized = normalized.Substring(0, maxLength);
            }

            return normalized;
        }
    }
}
