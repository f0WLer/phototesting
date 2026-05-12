using ProtoBuf;

namespace Phototesting.CameraCapture.Contracts
{
    // Protobuf packet DTOs for camera capture messaging.
    // Keep these classes data-only and preserve ProtoMember ids for compatibility.
    [ProtoContract]
    public class CameraLoadPlatePacket
    {
        [ProtoMember(1)]
        public bool Load { get; set; }
    }

    [ProtoContract]
    public class PhotoCaptureConfigPacket
    {
        [ProtoMember(1)]
        public int MaxDimension { get; set; }
    }

    [ProtoContract]
    public class PhotoCaptureConfigRequestPacket { }

    [ProtoContract]
    public class PhotoTakenPacket
    {
        [ProtoMember(1)]
        public string PhotoId { get; set; } = string.Empty;

        [ProtoMember(2)]
        public float HoldStillSeconds { get; set; }

        [ProtoMember(3)]
        public float HoldStillMovement { get; set; }
    }
}