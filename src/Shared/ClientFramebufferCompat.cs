using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Phototesting
{
    internal static class ClientFramebufferCompat
    {
        internal static FrameBufferRef Create(ICoreClientAPI capi, FramebufferAttrs attrs)
        {
            return ((ClientPlatformWindows)((ClientMain)capi.World).Platform).CreateFramebuffer(attrs);
        }
    }
}