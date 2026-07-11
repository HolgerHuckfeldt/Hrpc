namespace WaevaRpcCore.Attributes
{

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class WaevaRpcControllerAttribute : System.Attribute
    {
        public WaevaRpcControllerAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
