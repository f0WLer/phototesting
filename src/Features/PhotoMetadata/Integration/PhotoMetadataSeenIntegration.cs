using Phototesting.PhotoMetadata.Runtime;

namespace Phototesting.PhotoMetadata.Integration
{
    // Integration seam for seen-index touch semantics used by non-owner callsites.
    internal static class PhotoMetadataSeenIntegration
    {
        internal static void TouchPhotoSeen(ServerPhotoSeenService? service, string? photoId)
        {
            string normalized = PhotoMetadataPolicy.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(normalized)) return;

            service?.Touch(normalized);
        }
    }
}
