using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Phototesting.CameraCapture.Contracts;

namespace Phototesting.CameraCapture.Integration
{
    // CameraCapture packet DTO registration and channel handler wiring.
    internal static class CameraCaptureChannelRegistration
    {
        // Registers CameraCapture packet DTOs on the shared channel, preserving wire-order invariants.
        internal static INetworkChannel RegisterCameraCaptureMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(PhotoTakenPacket))
                .RegisterMessageType(typeof(CameraLoadPlatePacket))
                .RegisterMessageType(typeof(ExposureStatePacket));
        }

        // Registers CameraCapture config packet DTOs after sync packet DTOs to preserve existing wire order.
        internal static INetworkChannel RegisterCameraCaptureConfigMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(PhotoCaptureConfigRequestPacket))
                .RegisterMessageType(typeof(PhotoCaptureConfigPacket));
        }

        // Wires the client-side capture-config packet handler.
        internal static void ConfigureClientHandlers(
            IClientNetworkChannel channel,
            NetworkServerMessageHandler<PhotoCaptureConfigPacket> onPhotoCaptureConfigReceived)
        {
            if (channel == null || onPhotoCaptureConfigReceived == null) return;

            channel.SetMessageHandler<PhotoCaptureConfigPacket>(onPhotoCaptureConfigReceived);
        }

        // Wires server-side packet handlers used by camera authority paths.
        internal static void ConfigureServerCoreHandlers(
            IServerNetworkChannel channel,
            NetworkClientMessageHandler<PhotoTakenPacket> onPhotoTakenReceived,
            NetworkClientMessageHandler<CameraLoadPlatePacket> onCameraLoadPlateReceived,
            NetworkClientMessageHandler<ExposureStatePacket> onExposureStateReceived)
        {
            if (channel == null) return;

            if (onPhotoTakenReceived != null)
                channel.SetMessageHandler<PhotoTakenPacket>(onPhotoTakenReceived);

            if (onCameraLoadPlateReceived != null)
                channel.SetMessageHandler<CameraLoadPlatePacket>(onCameraLoadPlateReceived);

            if (onExposureStateReceived != null)
                channel.SetMessageHandler<ExposureStatePacket>(onExposureStateReceived);
        }

        // Wires server-side capture-config request handler.
        internal static void ConfigureServerSyncHandlers(
            IServerNetworkChannel channel,
            NetworkClientMessageHandler<PhotoCaptureConfigRequestPacket> onPhotoCaptureConfigRequested)
        {
            if (channel == null || onPhotoCaptureConfigRequested == null) return;

            channel.SetMessageHandler<PhotoCaptureConfigRequestPacket>(onPhotoCaptureConfigRequested);
        }
    }
}