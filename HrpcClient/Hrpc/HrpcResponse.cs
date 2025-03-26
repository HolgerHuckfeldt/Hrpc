using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc
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
    }
}
