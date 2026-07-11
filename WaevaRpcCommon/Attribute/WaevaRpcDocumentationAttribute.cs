using System;

namespace WaevaRpcCommon.Attribute
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
    public class WaevaRpcDocumentationAttribute : System.Attribute
    {
        public string Summary { get; }

        public WaevaRpcDocumentationAttribute(string summary)
        {
            Summary = summary;
        }
    }
}
