using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HrpcCore.Model.Messages.Mex
{
    public class DiscoveryInfo
    {
        public List<ControllerMeta> Controllers { get; set; } = new List<ControllerMeta>();
        public Dictionary<string, TypeMeta> Types { get; set; } = new Dictionary<string, TypeMeta>(StringComparer.OrdinalIgnoreCase);
    }
}
