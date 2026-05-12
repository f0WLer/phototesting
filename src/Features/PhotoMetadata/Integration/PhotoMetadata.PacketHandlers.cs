using Vintagestory.API.Server;
using Phototesting.PhotoMetadata.Runtime;
using Phototesting.PhotoSync.Contracts;

namespace Phototesting.PhotoMetadata.Integration
{
    // Server-side metadata packet semantics (seen-touch handling).
    // Caption-set handling is intentionally absent: it requires a placed-photo block entity to write caption
    // state into, which the framed-photograph display system used to provide. After that system was removed
    // in preparation for the kos-photographic-memories merge, caption editing is a dangling wire — the new
    // frame block entity from that mod (or a successor) is expected to register its own handler for
    // PhotoCaptionSetPacket on the shared channel.
    internal static class PhotoMetadataModSystemBridge
    {
        // Registers server-side metadata packet handlers on the shared transport channel.
        internal static void ConfigureServerPhotoMetadataChannelHandlers(PhotoTestingModSystem owner)
        {
            if (owner.ServerChannel == null) return;

            owner.ServerChannel
                .SetMessageHandler<PhotoSeenPacket>((player, packet) => HandlePhotoMetadataSeenPacket(owner, player, packet));
        }

        internal static void HandlePhotoMetadataSeenPacket(PhotoTestingModSystem owner, IServerPlayer player, PhotoSeenPacket packet)
        {
            if (packet == null || player == null) return;

            // Normalize and validate the photo id before mutating the seen index so junk ids cannot create entries.
            // The seen index is bounded by the number of valid photo ids on the server, so per-player rate limiting
            // would be belt-and-suspenders here — id validation is the only defence that actually matters.
            string photoId = PhotoMetadataPolicy.NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrEmpty(photoId)) return;

            owner.PhotoSyncBridge.ServerTouchPhotoSeen(photoId);
        }
    }
}
