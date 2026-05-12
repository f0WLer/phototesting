using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle.Tray
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        // Queues exactly one main-thread tray mesh rebuild even if multiple dirty signals arrive in quick succession.
        private void RequestClientMeshRebuild()
        {
            if (Api?.Side != EnumAppSide.Client) return;

            ICoreClientAPI capi = (ICoreClientAPI)Api;
            lock (_clientMeshLock)
            {
                if (_clientMeshQueued) return;
                _clientMeshQueued = true;
            }

            try
            {
                capi.Event.EnqueueMainThreadTask(() =>
                {
                    lock (_clientMeshLock) _clientMeshQueued = false;
                    BuildClientMesh(capi);
                }, "phototesting-devtray-rebuild");
            }
            catch (Exception ex)
            {
                Log.Debug(capi.Logger, "RequestClientMeshRebuild enqueue failed: {0}", ex.Message);
                lock (_clientMeshLock) _clientMeshQueued = false;
            }
        }

        // Rebuilds the cached tray body, plate/photo, and overlay meshes from the current authoritative tray state.
        private void BuildClientMesh(ICoreClientAPI capi)
        {
            if (capi == null) return;

            ItemStack? plate;
            string? sig;
            lock (_plateLock)
            {
                plate = PlateStack?.Clone();
                sig = BlockEntityDevelopmentTray.ComputePlateSignature(PlateStack);
            }

            if (plate?.Collectible?.Code == null)
            {
                lock (_clientMeshLock)
                {
                    _clientTrayBodyMesh = null;
                    _clientPhotoMesh = null;
                    _clientDeveloperOverlayMesh = null;
                }
                _clientRenderSignature = sig;
                MarkDirty(true);
                return;
            }

            bool builtTrayBody = TryBuildTrayBodyMesh(capi, out MeshData? trayBodyMesh);
            bool builtPhotoMesh = TryBuildPlateMesh(capi, plate, out MeshData? photoMesh);

            lock (_clientMeshLock)
            {
                if (builtTrayBody) _clientTrayBodyMesh = trayBodyMesh;
                _clientPhotoMesh = builtPhotoMesh ? photoMesh : null;
            }

            bool builtOverlayMesh = TryBuildPourOverlayMesh(capi, out MeshData? devOverlayMesh);

            lock (_clientMeshLock)
            {
                if (builtOverlayMesh)
                    _clientDeveloperOverlayMesh = devOverlayMesh;
                else if (!_clientDeveloperOverlayActive)
                    _clientDeveloperOverlayMesh = null;
            }

            _clientRenderSignature = sig;
            MarkDirty(true);
        }
    }
}
