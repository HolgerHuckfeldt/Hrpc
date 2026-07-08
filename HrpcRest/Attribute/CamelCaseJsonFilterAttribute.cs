namespace HrpcRest.Attribute
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using System.Text.Json;

    public class CamelCaseJsonFilterAttribute : ActionFilterAttribute
    {
        public override void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is ObjectResult objectResult)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
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
