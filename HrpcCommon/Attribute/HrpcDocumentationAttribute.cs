using System;

namespace HrpcCommon.Attribute
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
    public class HrpcDocumentationAttribute : System.Attribute
    {
        public string Summary { get; }

        public HrpcDocumentationAttribute(string summary)
        {
            Summary = summary;
        }
    }
}
