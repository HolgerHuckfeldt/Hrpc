using MessagePack;

namespace HrpcCore.Model.Messages
{
    [MessagePackObject]
    public class HrpcRequest
    {
        [Key(0)]
        public string Controller { get; set; } = string.Empty;
        [Key(1)]
        public string Method { get; set; } = string.Empty;
        [Key(2)]
        public string? StringData { get; set; }
        [Key(3)]
        public byte[]? BinaryData { get; set; }
        [Key(4)]
        public string? Id { get; set; } = string.Empty;

        [Key(5)]
        public Dictionary<string, string>? DependencyMapping { get; set; }
    }

}
