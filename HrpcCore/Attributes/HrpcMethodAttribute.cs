namespace HrpcCore.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class HrpcMethodAttribute : System.Attribute
    {
        private readonly string _name;

        public HrpcMethodAttribute(string name)
        {
            _name = name;
        }

        public string Name => _name;
    }
}
