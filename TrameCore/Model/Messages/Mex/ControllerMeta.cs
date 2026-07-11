using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrameCore.Model.Messages.Mex
{
    public class ControllerMeta
    {
        public string Name { get; set; } = string.Empty;
        public List<MethodMeta> Methods { get; set; } = new();
    }

    public class MethodMeta
    {
        public string MethodName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        // Optional: Falls der Rückgabetyp eine eigene Klasse ist, liefern wir auch deren Properties.
        public TypeMeta? ReturnTypeDefinition { get; set; }
        public List<ParameterMeta> Parameters { get; set; } = new();
        public string? Documentation { get; set; }

    }

    public class ParameterMeta
    {
        public string ParameterName { get; set; } = string.Empty;
        public string ParameterType { get; set; } = string.Empty;
        // Falls der Parameter ein benutzerdefinierter Typ ist, können hier die Properties mitgeliefert werden.
        public TypeMeta? TypeDefinition { get; set; }
        public string? Documentation { get; set; }
    }

    public class TypeMeta
    {
        public string TypeName { get; set; } = string.Empty;
        public List<PropertyMeta> Properties { get; set; } = new();

        public object? Example { get; set; }
    }

    public class PropertyMeta
    {
        public string PropertyName { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public TypeMeta? NestedTypeDefinition { get; set; } // Optional, um verschachtelte Metadaten zu speichern
    }


}
