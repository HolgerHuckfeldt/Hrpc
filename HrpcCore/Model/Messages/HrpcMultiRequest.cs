using HrpcCore.Model.Handler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HrpcCore.Model.Messages
{
    public class HrpcMultiRequest
    {
        public List<HrpcRequest>? Requests { get; set; }

        public ExecutionMode Mode { get; set; } = ExecutionMode.Serial;

    }
}
