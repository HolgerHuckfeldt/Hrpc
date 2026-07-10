namespace HrpcRest.Attribute
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using System.Text.Encodings.Web;
    using System.Text.Json;

    public class CamelCaseJsonFilterAttribute : ActionFilterAttribute
    {
        public override void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is ObjectResult objectResult)
            {
                // Kompakt (WriteIndented=false) + relaxed Encoder: Data ist jetzt ein
                // strukturiertes JsonElement und wird roh in einem Pass serialisiert —
                // kein Double-Wrapping mehr, keine `"`-Escape-Tax.
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                context.Result = new JsonResult(objectResult.Value, options)
                {
                    StatusCode = objectResult.StatusCode
                };
            }
            base.OnResultExecuting(context);
        }
    }
}
