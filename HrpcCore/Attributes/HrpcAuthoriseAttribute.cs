using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace HrpcCore.Attributes
{
    /// <summary>
    /// Beispielhaftes Authorize-Attribute für Hrpc.
    /// Prüft bspw. nur, ob der Nutzer authentifiziert ist und/oder
    /// eine bestimmte Rolle hat.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HrpcAuthoriseAttribute : Attribute
    {
        /// <summary>
        /// Optionale Rolle, die ein User haben muss, damit der Aufruf erlaubt ist.
        /// </summary>
        public string? Role { get; set; }

        public HrpcAuthoriseAttribute()
        {
        }

        public HrpcAuthoriseAttribute(string role)
        {
            Role = role;
        }

        /// <summary>
        /// Die Methode wird im HrpcService vor dem eigentlichen Methodenaufruf aufgerufen.
        /// Gibt true zurück, wenn autorisiert; false sonst.
        /// </summary>
        public virtual Task<bool> OnAuthorization(HttpContext? context)
        {
            // Ohne HttpContext keine Autorisierungsinfo -> verweigern
            if (context == null)
                return Task.FromResult(false);

            // Ist der User überhaupt authentifiziert?
            if (!context.User.Identity?.IsAuthenticated ?? false)
                return Task.FromResult(false);

            // Prüfen wir zusätzlich eine Rolle?
            if (!string.IsNullOrEmpty(Role))
            {
                // Falls der User nicht in dieser Rolle ist -> false
                if (!context.User.IsInRole(Role))
                {
                    return Task.FromResult(false);
                }
            }

            // Falls wir hier ankommen, hat der User alles erfüllt
            return Task.FromResult(true);
        }
    }
}
