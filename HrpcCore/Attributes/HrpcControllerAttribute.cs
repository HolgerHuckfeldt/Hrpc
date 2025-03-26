namespace HrpcCore.Attributes
{

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class HrpcControllerAttribute : System.Attribute
    {
        public HrpcControllerAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
