namespace TrameCore.Attributes
{

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class TrameControllerAttribute : System.Attribute
    {
        public TrameControllerAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
