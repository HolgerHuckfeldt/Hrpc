using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc.Exceptions
{
    public class HrpcException : Exception
    {
        public HrpcException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }

        public HrpcException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
