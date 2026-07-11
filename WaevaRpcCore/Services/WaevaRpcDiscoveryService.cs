using WaevaRpcCommon.Attribute;
using WaevaRpcCore.Attributes;
using WaevaRpcCore.Model.Messages.Mex;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace WaevaRpcCore.Services
{
    public class WaevaRpcDiscoveryService
    {
        private readonly ConcurrentDictionary<string, Type> _routeHandlers;
        private DiscoveryInfo? _cachedDiscovery;
        private readonly object _cacheLock = new();

        public WaevaRpcDiscoveryService(ConcurrentDictionary<string, Type> routeHandlers)
        {
            _routeHandlers = routeHandlers;
        }

        public DiscoveryInfo GetDiscoveryInfo()
        {
            if (_cachedDiscovery != null)
                return _cachedDiscovery;

            lock (_cacheLock)
            {
                return _cachedDiscovery ??= BuildDiscoveryInfo();
            }
        }

        public void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedDiscovery = null;
            }
        }

        private DiscoveryInfo BuildDiscoveryInfo()
        {
            var discovery = new DiscoveryInfo();

            foreach (var kvp in _routeHandlers)
            {
                string controllerName = kvp.Key;
                Type controllerType = kvp.Value;

                var controllerMeta = new ControllerMeta { Name = controllerName };

                // Erfasse alle Methoden, die mit [WaevaRpcMethodAttribute] dekoriert sind.
                var methods = controllerType.GetMethods()
                    .Where(m => m.GetCustomAttributes(typeof(WaevaRpcMethodAttribute), false).Any());

                foreach (var method in methods)
                {
                    // Verwende für die Discovery den Namen aus dem Attribut (falls vorhanden),
                    // damit dieser konsistent mit der Invoker-Registrierung ist.
                    var methodAttr = method.GetCustomAttribute<WaevaRpcMethodAttribute>();

                    // Bestimme den effektiven Rückgabetyp (Task entpacken)
                    Type effectiveReturnType = method.ReturnType;
                    if (typeof(Task).IsAssignableFrom(effectiveReturnType))
                    {
                        if (effectiveReturnType.IsGenericType)
                            effectiveReturnType = effectiveReturnType.GenericTypeArguments[0];
                        else
                            effectiveReturnType = typeof(void);
                    }

                    var methodDoc = method.GetCustomAttribute<WaevaRpcDocumentationAttribute>()?.Summary;

                    var methodMeta = new MethodMeta
                    {
                        Documentation = methodDoc,
                        MethodName = methodAttr?.Name ?? method.Name,
                        ReturnType = GetFriendlyTypeName(effectiveReturnType)
                    };

                    // Sonderfall: Wenn der Rückgabetyp eine List<T> ist, registriere das Generic-Argument
                    if (effectiveReturnType.IsGenericType && effectiveReturnType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        var genericArg = effectiveReturnType.GetGenericArguments()[0];
                        // Setze den Rückgabetyp als z. B. "List<Order>"
                        methodMeta.ReturnType = $"List<{GetFriendlyTypeName(genericArg)}>";
                        // Registriere das Generic-Argument, also z. B. Order
                        RegisterType(genericArg, discovery.Types);
                        // Falls das Generic-Argument als WaevaRpcDataContract markiert ist, setze die Nested-Definition.
                        // Primitive Generic-Argumente (z. B. List<int>) werden von RegisterType nicht registriert
                        // und haben daher keine TypeDefinition — in diesem Fall bleibt ReturnTypeDefinition null.
                        string key = genericArg.FullName ?? genericArg.Name;
                        if (key != null && discovery.Types.TryGetValue(key, out var returnTypeDef))
                            methodMeta.ReturnTypeDefinition = returnTypeDef;
                    }
                    // Für nicht-generische Rückgabetypen (außer void)
                    else if (!effectiveReturnType.Equals(typeof(void)) &&
                        effectiveReturnType.IsClass &&
                        effectiveReturnType != typeof(string) &&
                        effectiveReturnType.IsDefined(typeof(WaevaRpcDataContractAttribute), false))
                    {
                        RegisterType(effectiveReturnType, discovery.Types);
                        string key = effectiveReturnType.FullName ?? effectiveReturnType.Name;
                        if (key != null)
                            methodMeta.ReturnTypeDefinition = discovery.Types[key];
                    }

                    var paramdDoc = method.GetCustomAttribute<WaevaRpcDocumentationAttribute>()?.Summary;

                    // Parameter verarbeiten (CancellationToken ausschließen)
                    var parameters = method.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken));
                    
                    foreach (var param in parameters)
                    {
                        var paramMeta = new ParameterMeta
                        {
                            Documentation = paramdDoc,
                            ParameterName = param.Name ?? string.Empty,
                            ParameterType = GetFriendlyTypeName(param.ParameterType)
                        };

                        // Sonderfall: generische Typen (z. B. List<T>) bei Parametern
                        if (param.ParameterType.IsGenericType && param.ParameterType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            var genericArg = param.ParameterType.GetGenericArguments()[0];
                            paramMeta.ParameterType = $"List<{GetFriendlyTypeName(genericArg)}>";
                            RegisterType(genericArg, discovery.Types);
                            // Primitive Generic-Argumente (z. B. List<int>) werden nicht registriert und
                            // haben keine TypeDefinition — siehe Sonderfall beim Rückgabetyp oben.
                            string key = genericArg.FullName ?? genericArg.Name;
                            if (key != null && discovery.Types.TryGetValue(key, out var paramTypeDef))
                                paramMeta.TypeDefinition = paramTypeDef;
                        }
                        else if (param.ParameterType.IsClass && param.ParameterType != typeof(string) &&
                                 param.ParameterType.IsDefined(typeof(WaevaRpcDataContractAttribute), false))
                        {
                            RegisterType(param.ParameterType, discovery.Types);
                            string key = param.ParameterType.FullName ?? param.ParameterType.Name;
                            if (key != null)
                                paramMeta.TypeDefinition = discovery.Types[key];
                        }

                        methodMeta.Parameters.Add(paramMeta);
                    }

                    controllerMeta.Methods.Add(methodMeta);
                }

                discovery.Controllers.Add(controllerMeta);
            }

            return discovery;
        }


        private static TypeMeta GetTypeMeta(Type type, Dictionary<string, TypeMeta> types, HashSet<Type>? visited = null)
        {
            visited ??= new HashSet<Type>();

            // Für primitive Typen, Enums oder String: Nur den Typnamen zurückgeben.
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            {
                return new TypeMeta { TypeName = type.FullName ?? type.Name };
            }

            // Falls der Typ nicht als WaevaRpcDataContract markiert ist, nur den Typnamen zurückgeben.
            if (!type.IsDefined(typeof(WaevaRpcDataContractAttribute), inherit: false))
            {
                return new TypeMeta { TypeName = type.FullName ?? type.Name };
            }

            // Verhindere rekursive Zyklen.
            if (!visited.Add(type))
            {
                return new TypeMeta { TypeName = type.FullName ?? type.Name };
            }

            var typeMeta = new TypeMeta { TypeName = type.FullName ?? type.Name };

            // Prüfe, ob der Typ mit WaevaRpcExampleAttribute dekoriert ist.
            var exampleAttr = type.GetCustomAttribute<WaevaRpcExampleAttribute>();
            if (exampleAttr != null)
            {
                try
                {
                    // Deserialisiere den JSON-String in eine Instanz des Typs.
                    typeMeta.Example = JsonSerializer.Deserialize(exampleAttr.ExampleJson, type, new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    });
                }
                catch
                {
                    // Falls die Deserialisierung fehlschlägt, bleibt Example null.
                    typeMeta.Example = null;
                }
            }
            else
            {
                try
                {
                    // Wenn kein explizites Beispiel vorhanden ist, versuche eine Standardinstanz zu erzeugen.
                    typeMeta.Example = Activator.CreateInstance(type);
                }
                catch
                {
                    typeMeta.Example = null;
                }
            }

            // Verarbeite die Properties des Typs.
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                var propertyMeta = new PropertyMeta
                {
                    PropertyName = prop.Name,
                    PropertyType = GetFriendlyTypeName(prop.PropertyType)
                };

                // Für generische Typen: rekursiv die Generic-Argumente verarbeiten.
                if (prop.PropertyType.IsGenericType)
                {
                    foreach (var arg in prop.PropertyType.GetGenericArguments())
                    {
                        if (!(arg.IsPrimitive || arg.IsEnum || arg == typeof(string)) &&
                            arg.IsDefined(typeof(WaevaRpcDataContractAttribute), false))
                        {
                            RegisterType(arg, types, visited);
                            propertyMeta.NestedTypeDefinition = GetTypeMeta(arg, types, visited);
                        }
                    }
                }
                // Für komplexe Klassen (außer string), die als WaevaRpcDataContract markiert sind.
                else if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string) &&
                         prop.PropertyType.IsDefined(typeof(WaevaRpcDataContractAttribute), false))
                {
                    RegisterType(prop.PropertyType, types, visited);
                    propertyMeta.NestedTypeDefinition = GetTypeMeta(prop.PropertyType, types, visited);
                }

                typeMeta.Properties.Add(propertyMeta);
            }

            return typeMeta;
        }


        private static string GetFriendlyTypeName(Type type)
        {
            if (type.IsEnum)
            {
                return type.Name;
            }
            if (type.IsGenericType)
            {
                return GetFriendlyGenericTypeName(type);
            }
            return MapDotNetTypeName(type.FullName ?? type.Name);
        }

        private static string GetFriendlyGenericTypeName(Type type)
        {
            string genericTypeName = type.GetGenericTypeDefinition().Name;
            int backtickIndex = genericTypeName.IndexOf('`');
            if (backtickIndex > 0)
            {
                genericTypeName = genericTypeName.Substring(0, backtickIndex);
            }
            var genericArgs = type.GetGenericArguments().Select(arg => GetFriendlyTypeName(arg));
            string args = string.Join(", ", genericArgs);
            return $"{genericTypeName}<{args}>";
        }


        private static object? CreateExampleInstance(Type type)
        {
            try
            {
                // Erzeuge eine Basisinstanz
                var instance = Activator.CreateInstance(type);
                if (instance == null)
                    return null;

                // Für alle public, setzbare Properties, die als WaevaRpcDataContract markiert sind
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(p => p.CanWrite))
                {
                    // Nur für Klassen (außer string) und solche, die als WaevaRpcDataContract markiert sind
                    if (prop.PropertyType != typeof(string)
                        && prop.PropertyType.IsClass
                        && prop.PropertyType.IsDefined(typeof(WaevaRpcDataContractAttribute), false))
                    {
                        // Wenn der Wert null ist, versuche, ihn rekursiv zu erstellen
                        var current = prop.GetValue(instance);
                        if (current == null)
                        {
                            var nested = CreateExampleInstance(prop.PropertyType);
                            prop.SetValue(instance, nested);
                        }
                    }
                }
                return instance;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Registriert den Typ (bzw. das Generic-Argument) im Dictionary, sofern er mit [WaevaRpcDataContract] markiert ist.
        /// Dabei wird der vollqualifizierte Name als Schlüssel genutzt.
        /// </summary>
        // Passe auch RegisterType an, sodass es das gleiche HashSet weitergibt:
        private static void RegisterType(Type type, Dictionary<string, TypeMeta> types, HashSet<Type>? visited = null)
        {
            // Für generische Typen wie List<T> oder Dictionary<,> registrieren wir die Generic-Argumente.
            if (type.IsGenericType)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    if (!(arg.IsPrimitive || arg.IsEnum || arg == typeof(string)) &&
                        arg.IsDefined(typeof(WaevaRpcDataContractAttribute), false))
                    {
                        RegisterType(arg, types, visited);
                    }
                }
                // Optional: Wenn du den generischen Container selbst registrieren möchtest, kannst du das hier hinzufügen.
                return;
            }

            // Nur für Klassen, die als WaevaRpcDataContract markiert sind
            if (type.IsClass && type != typeof(string) && type.IsDefined(typeof(WaevaRpcDataContractAttribute), false))
            {
                string typeName = type.FullName ?? type.Name;
                if (!types.ContainsKey(typeName))
                {
                    var typeMeta = GetTypeMeta(type, types, visited);
                    types[typeName] = typeMeta;
                    AddTypeRecursive(types, typeMeta, visited);
                }
            }
        }

        private static void AddTypeRecursive(Dictionary<string, TypeMeta> types, TypeMeta typeMeta, HashSet<Type>? visited)
        {
            foreach (var prop in typeMeta.Properties)
            {
                if (prop.NestedTypeDefinition != null)
                {
                    string key = prop.NestedTypeDefinition.TypeName;
                    if (!types.ContainsKey(key))
                    {
                        types[key] = prop.NestedTypeDefinition;
                        AddTypeRecursive(types, prop.NestedTypeDefinition, visited);
                    }
                }
            }
        }

        private static string MapDotNetTypeName(string typeName)
        {
            // Eine einfache Mapping-Tabelle für gängige Typen
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"System.String", "string"},
                {"System.Int32", "int"},
                {"System.Int64", "long"},
                {"System.Boolean", "bool"},
                {"System.Double", "double"},
                {"System.Decimal", "decimal"},
                {"System.Single", "float"},
                {"System.DateTime", "DateTime"}
                // Weitere Typen können hier ergänzt werden.
            };

            if (mapping.TryGetValue(typeName, out var friendlyName))
            {
                return friendlyName;
            }

            // Falls der Typ nicht in der Tabelle ist, entferne den Namespace und gebe den einfachen Namen zurück.
            int lastDot = typeName.LastIndexOf('.');
            return lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;
        }
    }
}
