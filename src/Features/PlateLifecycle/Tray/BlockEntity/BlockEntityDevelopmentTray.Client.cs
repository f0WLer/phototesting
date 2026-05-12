using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Tray
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        private const byte DeveloperOverlayAlpha = 210;
        private const float DeveloperOverlayScale = 1.32f;
        private const float DeveloperOverlayAlphaStart = 1.0f;
        private const float DeveloperOverlayAlphaEnd = 0.35f;

        private static readonly AssetLocation _developerOverlayTextureAsset = new("phototesting", "textures/block/liquid/developer.png");
        private static readonly AssetLocation _developerOverlayAtlasKey = new("phototesting", "devtray-developer-overlay");
        private static readonly AssetLocation _fixerOverlayTextureAsset = new("phototesting", "textures/block/liquid/fixer.png");
        private static readonly AssetLocation _fixerOverlayAtlasKey = new("phototesting", "devtray-fixer-overlay");
        private static readonly AssetLocation _waterOverlayTextureAsset = new("survival", "textures/block/liquid/waterportion.png");
        private static readonly AssetLocation _waterOverlayAtlasKey = new("phototesting", "devtray-water-overlay");

        private sealed class PlatePhotoTextureSource : ITexPositionSource
        {
            private readonly ITexPositionSource _baseSource;
            private readonly TextureAtlasPosition _photoTex;

            public PlatePhotoTextureSource(ITexPositionSource baseSource, TextureAtlasPosition photoTex)
            {
                this._baseSource = baseSource;
                this._photoTex = photoTex;
            }

            public TextureAtlasPosition this[string textureCode]
            {
                get
                {
                    if (string.Equals(textureCode, "plate", StringComparison.OrdinalIgnoreCase))
                    {
                        return _photoTex;
                    }

                    return _baseSource[textureCode];
                }
            }

            public Size2i AtlasSize => _baseSource.AtlasSize;
        }

        private readonly object _clientMeshLock = new();
        private MeshData? _clientTrayBodyMesh;
        private MeshData? _clientFallbackTrayMesh; // body-only mesh built once at init; never nulled
        private MeshData? _clientPhotoMesh;
        private MeshData? _clientDeveloperOverlayMesh;
        private bool _clientMeshQueued;
        private bool _clientNeedsRebuild;
        private string? _clientRenderSignature;
        private bool _clientDeveloperOverlayActive;
        private float _clientDeveloperOverlayAlpha = 1.0f;
        private long _clientDeveloperOverlayLastRebuildMs;
        private string? _clientOverlayAction;

        // Sets up client-only tray rendering support, including the fallback body mesh and timed overlay polling.
        partial void ClientInitialize(ICoreAPI api)
        {
            if (api?.Side != EnumAppSide.Client) return;

            // Pre-build a tray body mesh (no plate element) so it is always available
            // during the threading race gap between a block-type change triggering chunk
            // retesselation and FromTreeAttributes finishing the full BuildClientMesh.
            // Without this the tray walls go blank for 1–2 frames while suppressing the
            // base block to prevent the sideways-plate flash.
            if (api is ICoreClientAPI capiInit && BlockTypeHasStaticPlate())
            {
                try
                {
                    ITexPositionSource bodySource = capiInit.Tesselator.GetTextureSource(Block);
                    var bodyShape = Block?.Shape?.Clone();
                    if (bodyShape != null)
                    {
                        bodyShape.IgnoreElements = new[] { "plate" };
                        capiInit.Tesselator.TesselateShape(
                            "phototesting-devtray-body-fallback",
                            Block?.Code ?? new AssetLocation("phototesting", "developmenttray-red"),
                            bodyShape,
                            out MeshData? fallbackMesh,
                            bodySource
                        );
                        lock (_clientMeshLock)
                        {
                            _clientFallbackTrayMesh = fallbackMesh;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(capiInit.Logger, "ClientInitialize: body mesh tessellation failed: {0}", ex.Message);
                }
            }

            // Poll local player state on the main thread and request re-tesselation when it changes.
            try
            {
                RegisterGameTickListener(_ =>
                {
                    if (Api?.Side != EnumAppSide.Client) return;
                    ICoreClientAPI capi = (ICoreClientAPI)Api;
                    long nowMs;
                    try
                    {
                        nowMs = (long)capi.World.ElapsedMilliseconds;
                    }
                    catch
                    {
                        nowMs = Environment.TickCount64;
                    }

                    bool shouldShow = TryGetPourOverlayAlpha(capi, out float alpha, out string action);
                    if (shouldShow)
                    {
                        bool actionChanged = !string.Equals(action, _clientOverlayAction, StringComparison.Ordinal);
                        bool alphaChanged = Math.Abs(alpha - _clientDeveloperOverlayAlpha) > 0.001f;
                        bool stale = nowMs - _clientDeveloperOverlayLastRebuildMs > 200;
                        if (_clientDeveloperOverlayActive && !alphaChanged && !stale && !actionChanged) return;

                        _clientDeveloperOverlayActive = true;
                        _clientDeveloperOverlayAlpha = alpha;
                        _clientDeveloperOverlayLastRebuildMs = nowMs;
                        _clientOverlayAction = action;
                        _clientNeedsRebuild = true;
                        RequestClientMeshRebuild();

                        try { capi.World.BlockAccessor.MarkBlockDirty(Pos); }
                        catch (Exception ex) { Log.Debug(capi.Logger, "overlay tick: MarkBlockDirty failed: {0}", ex.Message); }
                        return;
                    }

                    if (!_clientDeveloperOverlayActive) return;

                    _clientDeveloperOverlayActive = false;
                    _clientDeveloperOverlayLastRebuildMs = 0;
                    _clientOverlayAction = null;
                    _clientNeedsRebuild = true;
                    RequestClientMeshRebuild();

                    try { capi.World.BlockAccessor.MarkBlockDirty(Pos); }
                    catch (Exception ex) { Log.Debug(capi.Logger, "overlay tick: MarkBlockDirty failed: {0}", ex.Message); }
                }, 50);
            }
            catch (Exception ex)
            {
                Log.Warn(api.Logger, "ClientInitialize: failed to register overlay tick listener: {0}", ex.Message);
            }
        }

        // Rebuilds the cached client meshes immediately after plate or facing changes so the next tesselation sees a coherent tray state.
        partial void ClientPlateChanged(bool markBlockDirty)
        {
            if (Api?.Side != EnumAppSide.Client) return;
            if (Api is not ICoreClientAPI capi) return;

            lock (_clientMeshLock)
            {
                if (!_clientDeveloperOverlayActive)
                    _clientDeveloperOverlayMesh = null;
            }

            // Build ALL client meshes (body + plate/photo) fully and synchronously on the
            // main thread right now, before MarkDirty causes any tesselation. This ensures
            // the tesselation thread always sees both clientTrayBodyMesh and clientPhotoMesh
            // populated from the very first frame — no flash of the base block shape or
            // a missing plate while the async rebuild catches up.
            _clientNeedsRebuild = false;
            _clientRenderSignature = null;
            BuildClientMesh(capi);
            // BuildClientMesh calls MarkDirty(true) internally — no need to call it here.
        }

        // Serves cached tray meshes to the tesselation thread and requests a rebuild when the cached signature is stale.
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Api?.Side != EnumAppSide.Client)
            {
                return base.OnTesselation(mesher, tessThreadTesselator);
            }

            // OnTesselation runs on the tesselation thread: only read cached meshes here.
            string? sig = null;
            try
            {
                lock (_plateLock)
                {
                    sig = BlockEntityDevelopmentTray.ComputePlateSignature(PlateStack);
                }
            }
            catch
            {
                sig = null;
            }

            if (!string.Equals(_clientRenderSignature, sig, StringComparison.Ordinal))
            {
                _clientNeedsRebuild = true;
            }

            if (_clientNeedsRebuild)
            {
                _clientNeedsRebuild = false;
                RequestClientMeshRebuild();
            }

            MeshData? trayBodyMesh;
            MeshData? photoMesh;
            MeshData? overlayMesh;
            lock (_clientMeshLock)
            {
                trayBodyMesh = _clientTrayBodyMesh;
                photoMesh = _clientPhotoMesh;
                overlayMesh = _clientDeveloperOverlayMesh;
            }

            // When we have a tray body mesh we've taken full ownership of rendering:
            // add our custom meshes and return true to suppress the base block mesh.
            // clientTrayBodyMesh is built synchronously in ClientPlateChanged so it is
            // always ready by the time MarkBlockDirty triggers this method.
            if (trayBodyMesh != null)
            {
                mesher.AddMeshData(trayBodyMesh.Clone());
                if (photoMesh != null)
                {
                    mesher.AddMeshData(photoMesh.Clone());
                }
                if (overlayMesh != null)
                {
                    mesher.AddMeshData(overlayMesh.Clone());
                }
                return true;
            }

            // clientTrayBodyMesh is null — our sync build hasn't completed yet (a genuine
            // threading race: VS queues a retesselation the moment the block type changes,
            // which can fire before FromTreeAttributes finishes on the main thread).
            //
            // If the block code tells us a plate *must* be present (any loaded stage),
            // suppress the base block entirely and request a rebuild immediately.
            // This gives a blank tray for at most one or two frames — far less noticeable
            // than the static plate element snapping from sideways to upright.
            if (BlockTypeHasStaticPlate())
            {
                RequestClientMeshRebuild();
                // Render the pre-built fallback body so the tray walls stay visible
                // during the brief gap before the full rebuild completes.
                MeshData? fallback;
                lock (_clientMeshLock) { fallback = _clientFallbackTrayMesh; }
                if (fallback != null) mesher.AddMeshData(fallback.Clone());
                return true;
            }

            // No plate loaded — fall back to normal (base) block rendering.
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        // Checks whether this tray block variant includes a baked-in plate shape and therefore must suppress the base block mesh.
        private bool BlockTypeHasStaticPlate()
        {
            string path = Block?.Code?.Path ?? string.Empty;
            return path.Contains("-exposed", StringComparison.Ordinal)
                || path.Contains("-reclaimed", StringComparison.Ordinal)
                || path.Contains("-developed", StringComparison.Ordinal)
                || path.Contains("-finished", StringComparison.Ordinal);
        }

    }
}

