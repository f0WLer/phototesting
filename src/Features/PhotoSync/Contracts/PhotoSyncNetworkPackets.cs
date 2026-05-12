using ProtoBuf;

namespace Phototesting.PhotoSync.Contracts
{
    // Protobuf packet DTOs for photo sync transfer and persistence messaging.
    // Keep these classes data-only and preserve ProtoMember ids for compatibility.
    [ProtoContract]
    public class PhotoBlobAckPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;

        [ProtoMember(2)]
        public bool Ok;

        [ProtoMember(3)]
        public string Error = string.Empty;
    }

    [ProtoContract]
    public class PhotoBlobChunkPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;

        [ProtoMember(2)]
        public int TotalSize;

        [ProtoMember(3)]
        public int ChunkIndex;

        [ProtoMember(4)]
        public int ChunkCount;

        [ProtoMember(5)]
        public byte[] Data = System.Array.Empty<byte>();

        // true: client->server upload; false: server->client download
        [ProtoMember(6)]
        public bool IsUpload;

        // Absolute byte offset in the destination buffer for this chunk.
        [ProtoMember(7)]
        public int ChunkOffset;
    }

    [ProtoContract]
    public class PhotoBlobRequestPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;
    }

    [ProtoContract]
    public class PhotoCaptionSetPacket
    {
        [ProtoMember(1)]
        public int X;

        [ProtoMember(2)]
        public int Y;

        [ProtoMember(3)]
        public int Z;

        [ProtoMember(4)]
        public string Caption = string.Empty;
    }

    [ProtoContract]
    public class PhotoSeenPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;
    }
}
