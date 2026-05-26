using ProtoBuf;
using Vintagestory.API.Client;

namespace Phototesting.AdminTooling
{
    // Packet DTO and channel registration for AdminTooling network messages.
    internal static class AdminToolingChannelRegistration
    {
        internal static INetworkChannel RegisterAdminToolingMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(GiveSensitizedPlatePacket));
        }
    }

    /// <summary>Requests that the server spawn a fresh sensitized plate in the requesting player's inventory.</summary>
    [ProtoContract]
    internal class GiveSensitizedPlatePacket { }
}
