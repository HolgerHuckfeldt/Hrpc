using HrpcCore.Attributes;
using System.Reflection;

namespace HrpcCore.Model.Handler
{
    public class InvokeInfo
    {
        public MethodInfo? MethodInfo { get; set; }

        public List<Type>? ParamTypes { get; set; } = new ();

        public HrpcAuthoriseAttribute? AuthoriseAttribute { get; set; }
    }
}
