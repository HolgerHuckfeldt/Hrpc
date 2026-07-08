using System.Text.Json;

namespace HrpcClient.Hrpc
{
    public class HrpcCall
    {
        private readonly string _controller;
        private readonly string _method;
        //private string _dataPath;
        private string _name;
        private readonly List<HrpcParameter> _parameters = new();
        private int _num = 0;
        // Hier speichern wir die Abhängigkeiten, die dieser Aufruf "exposed" (bereitstellt)
        private readonly Dictionary<string, string> _exposedDependencies = new();

        private HrpcCall(string controller, string method)
        {
            _controller = controller;
            _method = method;
        }

        public static HrpcCall Init(string controller, string method)
        {
            return new HrpcCall(controller, method);
        }

        public HrpcCall Named(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Gibt an, dass aus dem Ergebnis dieser Methode der Wert, der unter dem angegebenen JSON‑Path zu finden ist,
        /// unter dem Alias zur Verfügung gestellt wird.
        /// Beispiel: Exposes("$[0].Id", "firstId")
        /// </summary>
        public HrpcCall Exposes(string jsonPath, string alias)
        {
            _exposedDependencies[alias] = jsonPath;
            return this;
        }

        /// <summary>
        /// Fügt einen Parameter mit einem Dependency-Platzhalter hinzu.
        /// Beispiel: WithAlias("@firstId")
        /// Der Platzhalter (z. B. "@firstId") wird unter dem abgeleiteten Parameternamen
        /// (ohne führendes @) hinterlegt. Die Server-Logik ersetzt diesen Platzhalter
        /// anhand der zuvor exposed Dependencies. Ist ein Alias nicht auflösbar, schlägt
        /// der Aufruf fehlt (kein impliziter Fallback in v1).
        /// </summary>
        public HrpcCall WithAlias(string dependencyPlaceholder)
        {
            // Store the alias placeholder with a parameter name derived from the alias.
            // The server resolves @alias references before invoking the method.
            var aliasName = dependencyPlaceholder.StartsWith('@')
                ? dependencyPlaceholder.Substring(1)
                : dependencyPlaceholder;
            _parameters.Add(new HrpcParameter
            {
                Num = _num,
                ParameterName = aliasName,
                Data = dependencyPlaceholder // e.g. "@firstId"
            });
            _num++;
            return this;
        }

        /// <summary>
        /// Fügt einen benannten Parameter hinzu. Der Name muss mit einem Parameter der
        /// Zielmethode übereinstimmen (sichere, positionsunabhängige Bindung).
        /// </summary>
        public HrpcCall Param(string parameterName, object? value)
        {
            _parameters.Add(new HrpcParameter
            {
                Num = _num,
                ParameterName = parameterName,
                Data = value == null ? null : JsonSerializer.Serialize(value)
            });
            _num++;
            return this;
        }

        /// <summary>
        /// Fügt einen oder mehrere Parameter hinzu.
        /// </summary>
        public HrpcCall With(params object?[] args)
        {
            foreach (var arg in args)
                Add(arg);
            return this;
        }

        /// <summary>
        /// Fügt einen einzelnen Parameter hinzu.
        /// </summary>
        public HrpcCall Add(object? p)
        {
            _parameters.Add(new HrpcParameter()
            {
                Num = _num,
                ParameterName = $"param{_num}",
                Data = p == null ? null : JsonSerializer.Serialize(p)
            });
            _num++;
            return this;
        }

        /// <summary>
        /// Wandelt den HrpcCall in einen HrpcRequest um.
        /// Optional könnte hier auch die Dependency-Mapping-Information mit übertragen werden.
        /// </summary>
        public HrpcRequest ToRequest()
        {
            if (string.IsNullOrEmpty(_name))
                _name = $"{_controller}.{_method}";

            return new HrpcRequest
            {
                Controller = _controller,
                Method = _method,
                StringData = JsonSerializer.Serialize(_parameters),
                Id = _name,
                //DataPath = _dataPath,
                DependencyMapping = _exposedDependencies.Count > 0 ? _exposedDependencies : null
            };
        }
    }
}
