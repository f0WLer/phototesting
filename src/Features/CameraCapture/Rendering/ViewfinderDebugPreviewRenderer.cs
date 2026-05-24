using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture;

namespace Phototesting.CameraCapture.Rendering
{
    // On-screen debug preview overlay.
    // Displays live frames from VirtualCameraPreviewRenderer — either idle vcam renders
    // (when DebugPreviewPeak is on and no exposure is active) or exposure-accumulation
    // frames pushed via IExposurePreviewSink during an active exposure session.
    internal sealed class ViewfinderDebugPreviewRenderer : IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private readonly VirtualCameraPreviewRenderer? _virtualPreviewRenderer;

        private LoadedTexture? _previewTexture;

        public ViewfinderDebugPreviewRenderer(ICoreClientAPI capi, VirtualCameraPreviewRenderer? virtualPreviewRenderer = null)
        {
            this._capi = capi;
            this._virtualPreviewRenderer = virtualPreviewRenderer;
        }

        private ViewfinderConfig? ViewfinderCfg => PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.Viewfinder;

        public double RenderOrder => 0.97;
        public int RenderRange => 0;

        // Periodically draws the preview overlay when DebugPreviewPeak is on and the
        // virtual preview renderer has content available (idle vcam or active exposure frames).
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Ortho) return;

            ViewfinderConfig? cfg = ViewfinderCfg;
            if (cfg == null) return;

            bool exposureActive = _virtualPreviewRenderer?.IsExposureActive == true;

            // Idle vcam preview requires peak mode; exposure accumulation always shows.
            if (!exposureActive && !cfg.DebugPreviewPeak) return;
            if (_virtualPreviewRenderer?.IsActive != true) return;

            bool hasNewFrame = _virtualPreviewRenderer!.TryConsumeLatestFrame(out int[] bgraPixels, out int width, out int height);

            if (hasNewFrame)
            {
                if (_previewTexture == null || _previewTexture.Width != width || _previewTexture.Height != height)
                {
                    _previewTexture?.Dispose();
                    _previewTexture = new LoadedTexture(_capi)
                    {
                        Width = width,
                        Height = height
                    };
                }

                _capi.Render.LoadOrUpdateTextureFromBgra(
                    bgraPixels,
                    linearMag: true,
                    clampMode: (int)EnumTextureWrap.ClampToEdge,
                    intoTexture: ref _previewTexture);

                // LoadOrUpdateTextureFromBgra updates level 0 via TexSubImage on existing textures.
                // Rebuild mipmaps so lower levels do not keep stale first-frame content.
                _capi.Render.BindTexture2d(_previewTexture.TextureId);
                _capi.Render.GlGenerateTex2DMipmaps();
            }

            if (_previewTexture == null || _previewTexture.TextureId == 0) return;

            int frameWidth = _capi.Render.FrameWidth;
            int frameHeight = _capi.Render.FrameHeight;

            int margin = cfg.DebugPreviewMargin;
            int previewWidth = Math.Max(64, Math.Min(cfg.DebugPreviewWidth, Math.Max(64, frameWidth - margin * 2)));
            int previewHeight = Math.Max(64, Math.Min(cfg.DebugPreviewHeight, Math.Max(64, frameHeight - margin * 2)));

            float x = margin;
            float y = margin;

            switch ((cfg.DebugPreviewAnchor ?? "topright").Trim().ToLowerInvariant())
            {
                case "topleft":
                    x = margin;
                    y = margin;
                    break;

                case "bottomleft":
                    x = margin;
                    y = frameHeight - margin - previewHeight;
                    break;

                case "bottomright":
                    x = frameWidth - margin - previewWidth;
                    y = frameHeight - margin - previewHeight;
                    break;

                case "topright":
                default:
                    x = frameWidth - margin - previewWidth;
                    y = margin;
                    break;
            }

            _capi.Render.GLDisableDepthTest();
            try
            {
                float boxW = previewWidth;
                float boxH = previewHeight;
                float texAspect = _previewTexture.Width > 0 && _previewTexture.Height > 0
                    ? _previewTexture.Width / (float)_previewTexture.Height
                    : (10f / 11f);
                float boxAspect = boxW / Math.Max(1f, boxH);

                float drawW = boxW;
                float drawH = boxH;
                if (texAspect > boxAspect)
                {
                    drawH = boxW / Math.Max(0.0001f, texAspect);
                }
                else
                {
                    drawW = boxH * texAspect;
                }

                float drawX = x + (boxW - drawW) * 0.5f;
                float drawY = y + (boxH - drawH) * 0.5f;

                // Matte fill so letterbox/pillarbox areas are explicit and not stale framebuffer content.
                _capi.Render.RenderRectangle(x, y, 49f, boxW, boxH, unchecked((int)0xFF000000));
                _capi.Render.Render2DTexture(_previewTexture.TextureId, drawX, drawY, drawW, drawH, 50f, ColorUtil.WhiteArgbVec);
                _capi.Render.RenderRectangle(x - 1, y - 1, 49f, boxW + 2, boxH + 2, ColorUtil.WhiteArgb);
            }
            finally
            {
                _capi.Render.GLEnableDepthTest();
            }
        }

        // Releases the preview texture when the renderer is shut down.
        public void Dispose()
        {
            _previewTexture?.Dispose();
            _previewTexture = null;
        }
    }
}
