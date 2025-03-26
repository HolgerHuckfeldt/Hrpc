using System;
using System.Collections.Generic;
using System.Text;

namespace HrpcCommon.Exceptions
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
