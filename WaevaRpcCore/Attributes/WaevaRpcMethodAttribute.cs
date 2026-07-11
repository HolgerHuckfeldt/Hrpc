namespace WaevaRpcCore.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class WaevaRpcMethodAttribute : System.Attribute
    {
        private readonly string _name;

        public WaevaRpcMethodAttribute(string name)
        {
            _name = name;
        }

        public string Name => _name;
    }
}
