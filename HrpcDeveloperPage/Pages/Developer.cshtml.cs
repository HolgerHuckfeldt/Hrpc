using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace HrpcDeveloperPage
{
    public class DeveloperModel : PageModel
    {
        public List<ControllerMeta> Controllers { get; set; } = new();
        public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            try
            {
                // Basis-URL aus dem aktuellen Request ableiten
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

                string discoveryEndpoint = "/api/hrpc/discovery";
                string json = await client.GetStringAsync(discoveryEndpoint);
                Controllers = JsonSerializer.Deserialize<List<ControllerMeta>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<ControllerMeta>();
            }
            catch (System.Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    // DTO-Klassen (oder referenziere sie aus deiner Hrpc-Library)
    public class ControllerMeta
    {
        public string Name { get; set; } = string.Empty;
        public List<MethodMeta> Methods { get; set; } = new();
    }
    public class MethodMeta
    {
        public string MethodName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public List<ParameterMeta> Parameters { get; set; } = new();
    }
    public class ParameterMeta
    {
        public string ParameterName { get; set; } = string.Empty;
        public string ParameterType { get; set; } = string.Empty;
    }
}
