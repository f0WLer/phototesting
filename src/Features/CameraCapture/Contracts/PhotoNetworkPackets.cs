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
    public class CameraTripodPacket
    {
        [ProtoMember(1)]
        public bool Mount { get; set; }
    }

    [ProtoContract]
    public class PhotoCaptureConfigPacket
    {
        [ProtoMember(1)]
        public int MaxDimension { get; set; }
    }

    [ProtoContract]
    public class PhotoCaptureConfigRequestPacket { }

    /// <summary>Requests that the server spawns a camera-mounted block at the player's position and moves the camera item into it. The exposure itself begins only when the player right-clicks the spawned block.</summary>
    [ProtoContract]
    public class CameraMountRequestPacket
    {
        [ProtoMember(1)] public double CameraPosX { get; set; }
        [ProtoMember(2)] public double CameraPosY { get; set; }
        [ProtoMember(3)] public double CameraPosZ { get; set; }
        [ProtoMember(4)] public float CameraYaw { get; set; }
        [ProtoMember(5)] public float CameraPitch { get; set; }
        [ProtoMember(6)] public float CameraFov { get; set; }
        [ProtoMember(7)] public int CameraDimension { get; set; }
        [ProtoMember(9)] public int StopMode { get; set; }
        [ProtoMember(10)] public float StopAfterSeconds { get; set; }
    }

    [ProtoContract]
    public class MountedCameraControlPacket
    {
        [ProtoMember(1)]
        public bool IsExposing { get; set; }

        [ProtoMember(2)] public string ExposureId { get; set; } = string.Empty;
        [ProtoMember(3)] public string ProcessId { get; set; } = string.Empty;
        [ProtoMember(4)] public bool HasCameraState { get; set; }
        [ProtoMember(5)] public double CameraPosX { get; set; }
        [ProtoMember(6)] public double CameraPosY { get; set; }
        [ProtoMember(7)] public double CameraPosZ { get; set; }
        [ProtoMember(8)] public float CameraYaw { get; set; }
        [ProtoMember(9)] public float CameraPitch { get; set; }
        [ProtoMember(10)] public float CameraFov { get; set; }
        [ProtoMember(11)] public int CameraDimension { get; set; }
        [ProtoMember(13)] public int StopMode { get; set; }
        [ProtoMember(14)] public float StopAfterSeconds { get; set; }
    }

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

    /// <summary>Tells the server to stamp the partial-exposure plate in the active slot with the given photo id and insert it into the development tray at the given position.</summary>
    [ProtoContract]
    internal class SealAndInsertIntoTrayPacket
    {
        [ProtoMember(1)] public string ExposureId { get; set; } = string.Empty;
        [ProtoMember(2)] public string PhotoId    { get; set; } = string.Empty;
        [ProtoMember(3)] public int TrayPosX      { get; set; }
        [ProtoMember(4)] public int TrayPosY      { get; set; }
        [ProtoMember(5)] public int TrayPosZ      { get; set; }
        [ProtoMember(6)] public int TrayPosDim    { get; set; }
    }
}