using System;

namespace HrpcCommon.Attribute
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class HrpcExampleAttribute : System.Attribute
    {
        public string ExampleJson { get; }

        public HrpcExampleAttribute(string exampleJson)
        {
            ExampleJson = exampleJson;
        }
    }
}
