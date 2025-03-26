using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc
{
    public class HrpcMultiRequest
    {
        public List<HrpcRequest>? Requests { get; set; }

        public ExecutionMode Mode { get; set; } = ExecutionMode.Serial;

    }
}
