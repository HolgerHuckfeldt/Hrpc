using System;

namespace Hrpc.Spike.LinqProvider;

/// <summary>
/// Markiert ein Service-Interface als Hrpc-Vertrag für einen Controller.
/// Wird vom ContractGenerator aus der Discovery auf das generierte Interface
/// gesetzt; der LINQ-Client liest hieraus den Controller-Namen für den Request.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class HrpcServiceContractAttribute : Attribute
{
    public string Controller { get; }
    public HrpcServiceContractAttribute(string controller) => Controller = controller;
}

/// <summary>
/// Markiert eine Vertragsmethode als Hrpc-Methode. Carries den serverseitigen
/// Methoden-Namen (der nicht zwingend dem C#-Methodennamen entsprechen muss).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HrpcMethodContractAttribute : Attribute
{
    public string Method { get; }
    public HrpcMethodContractAttribute(string method) => Method = method;
}