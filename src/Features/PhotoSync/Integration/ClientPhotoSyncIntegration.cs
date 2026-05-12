using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Phototesting.AdminTooling;
using Phototesting.PhotoSync.Runtime;

namespace Phototesting.PhotoSync.Integration
{
    // Feature seam: centralizes client-side photo sync access so non-feature code avoids direct PhotoSync reach-through.
    internal static class ClientPhotoSyncIntegration
    {
        private static PhotoAssetSync? ResolveClientPhotoSync(ICoreClientAPI capi)
        {
            return PhotoTestingConfigAccess.ResolveClientModSystem(capi)?.PhotoSyncBridge.Runtime;
        }

        internal static void MaybeSendPhotoSeen(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return;
            PhotoTestingConfigAccess.ResolveClientModSystem(capi)?.PhotoSyncBridge.ClientMaybeSendPhotoSeen(photoId);
        }

        internal static void NotifyPhotoCreated(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return;
            ResolveClientPhotoSync(capi)?.ClientOnPhotoCreated(photoId);
        }

        internal static void RequestPhotoIfMissing(ICoreClientAPI capi, string photoId)
        {
            if (capi == null || string.IsNullOrEmpty(photoId)) return;
            ResolveClientPhotoSync(capi)?.ClientRequestPhotoIfMissing(photoId);
        }

        internal static void NoteBlockWaitingForPhoto(ICoreClientAPI capi, string photoId, BlockPos pos)
        {
            if (capi == null || string.IsNullOrEmpty(photoId) || pos == null) return;
            ResolveClientPhotoSync(capi)?.ClientNoteBlockWaitingForPhoto(photoId, pos);
        }
    }
}
