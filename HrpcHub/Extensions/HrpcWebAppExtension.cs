
namespace HrpcHub.Extensions
{
    public static class HrpcWebAppExtension
    {
        public static HubEndpointConventionBuilder AddHrpc(
            this IEndpointRouteBuilder endpoints)
        {
            if (endpoints == null)
                throw new ArgumentNullException(nameof(endpoints));

            return endpoints.MapHub<Hub.HrpcHub>("/Hrpc");
        }
    }
}
