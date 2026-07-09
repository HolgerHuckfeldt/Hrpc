using Hrpc.Model;

namespace Hrpc.Spike.LinqProvider;

// =====================================================================
// AUTO-GENERIERT (Prototyp). Diese Datei spiegelt den Output des
// ContractGenerator (siehe ContractGenerator.cs) wider, der die Discovery
// (/api/hrpc/discovery) in typsichere C#-Verträge übersetzt. Im Prototyp
// ist sie der Einfachheit halber handgeführt und gegen den Customer-
// Controller der Sample-App gemodelt. Ein echter Generator würde für jede
// Discovery-Datei eine solche Datei erzeugen.
//
// Konvention: Rückgaben sind entpackt — Task<T> -> T, Task -> void,
// CancellationToken entfällt. Parametertypen sind Arg<T>-Wrapper, damit ein
// Parameter sowohl konkrete Werte als auch Dep<T>-Platzhalter (für Dependency-
// Chaining) annehmen kann — typgeprüft vom Compiler.
// =====================================================================

/// <summary>Vertrag für den Customer-Controller der Sample-App.</summary>
[HrpcServiceContract("Customer")]
public interface ICustomerService
{
    [HrpcMethodContract("AddCustomer")]
    Task<int> AddCustomer(Arg<string> name);

    [HrpcMethodContract("GetCustomerById")]
    Task<Customer?> GetCustomerById(Arg<int> id);

    [HrpcMethodContract("GetAllCustomers")]
    Task<List<Customer>> GetAllCustomers();

    [HrpcMethodContract("UpdateCustomerName")]
    Task UpdateCustomerName(Arg<int> customerId, Arg<string> newName);

    [HrpcMethodContract("DeleteCustomer")]
    Task DeleteCustomer(Arg<int> id);
}