using System;

namespace WaevaRpc.Spike.LinqProvider;

/// <summary>
/// Markiert ein Service-Interface als WaevaRpc-Vertrag für einen Controller.
/// Wird vom ContractGenerator aus der Discovery auf das generierte Interface
/// gesetzt; der LINQ-Client liest hieraus den Controller-Namen für den Request.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class WaevaRpcServiceContractAttribute : Attribute
{
    public string Controller { get; }
    public WaevaRpcServiceContractAttribute(string controller) => Controller = controller;
}

/// <summary>
/// Markiert eine Vertragsmethode als WaevaRpc-Methode. Carries den serverseitigen
/// Methoden-Namen (der nicht zwingend dem C#-Methodennamen entsprechen muss).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WaevaRpcMethodContractAttribute : Attribute
{
    public string Method { get; }
    public WaevaRpcMethodContractAttribute(string method) => Method = method;
}