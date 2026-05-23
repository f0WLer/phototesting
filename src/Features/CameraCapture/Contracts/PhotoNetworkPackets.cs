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
    }

    /// <summary>Notifies the server that the client's viewfinder accumulation state changed.</summary>
    [ProtoContract]
    public class ExposureStatePacket
    {
        /// <summary>True when exposure starts or resumes; false when it pauses.</summary>
        [ProtoMember(1)] public bool IsExposing { get; set; }
        /// <summary>Stable identifier for this plate's accumulation session.</summary>
        [ProtoMember(2)] public string ExposureId { get; set; } = string.Empty;
        /// <summary>Frames accumulated so far (sent on pause to keep the attribute current).</summary>
        [ProtoMember(3)] public int ExposedFrames { get; set; }
        /// <summary>Target sample count for a correct exposure (sent on start/resume).</summary>
        [ProtoMember(4)] public int TargetFrames { get; set; }
    }
}