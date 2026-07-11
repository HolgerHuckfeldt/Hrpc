
namespace WaevaRpcHub.Extensions
{
    public static class WaevaRpcWebAppExtension
    {
        public static HubEndpointConventionBuilder AddWaevaRpc(
            this IEndpointRouteBuilder endpoints)
        {
            if (endpoints == null)
                throw new ArgumentNullException(nameof(endpoints));

            return endpoints.MapHub<Hub.WaevaRpcHub>("/Waeva");
        }
    }
}
