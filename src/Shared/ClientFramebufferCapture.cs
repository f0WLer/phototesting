using System.Runtime.InteropServices;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace Phototesting
{
    internal static class ClientFramebufferCapture
    {
        internal static SKBitmap ReadToSkBitmap(ICoreClientAPI capi, FrameBufferRef framebuffer, bool withAlpha = false, bool flip = true)
        {
            ClientPlatformWindows platform = (ClientPlatformWindows)((ClientMain)capi.World).Platform;
            FrameBufferRef previousFramebuffer = platform.CurrentFrameBuffer;
            try
            {
                platform.CurrentFrameBuffer = framebuffer;
                BitmapRef screenshot = platform.GrabScreenshot(
                    framebuffer.Width,
                    framebuffer.Height,
                    scaleScreenshot: false,
                    flip: flip,
                    withAlpha: withAlpha);

                try
                {
                    return ToSkBitmap(screenshot, withAlpha);
                }
                finally
                {
                    screenshot.Dispose();
                }
            }
            finally
            {
                platform.CurrentFrameBuffer = previousFramebuffer;
            }
        }

        private static SKBitmap ToSkBitmap(BitmapRef bitmapRef, bool withAlpha)
        {
            var info = new SKImageInfo(
                bitmapRef.Width,
                bitmapRef.Height,
                SKColorType.Bgra8888,
                withAlpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque);

            var bitmap = new SKBitmap(info);
            Marshal.Copy(bitmapRef.Pixels, 0, bitmap.GetPixels(), bitmapRef.Pixels.Length);
            return bitmap;
        }
    }
}