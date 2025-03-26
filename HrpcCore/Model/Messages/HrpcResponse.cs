using MessagePack;

namespace HrpcCore.Model.Messages
{
    [MessagePackObject]
    public class HrpcResponse
    {
        [Key(0)]
        public int Code { get; set; }
        [Key(1)]
        public string? Data { get; set; }
        [Key(2)]
        public byte[]? Content { get; set; }
        [Key(3)]
        public string? Id { get; set; }
        // Neues Property für aufgelöste Abhängigkeiten (Alias -> Wert)
        [Key(4)]
        public Dictionary<string, string>? ExposedDependencies { get; set; }
    }
}
