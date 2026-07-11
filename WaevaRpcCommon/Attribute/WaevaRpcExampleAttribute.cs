using System;

namespace WaevaRpcCommon.Attribute
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class WaevaRpcExampleAttribute : System.Attribute
    {
        public string ExampleJson { get; }

        public WaevaRpcExampleAttribute(string exampleJson)
        {
            ExampleJson = exampleJson;
        }
    }
}
