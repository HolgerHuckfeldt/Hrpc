namespace HrpcRest.Attribute
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using System.Text.Json;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    public class CamelCaseJsonFilterAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// Gibt an, ob Newtonsoft.Json (true) oder System.Text.Json (false) verwendet werden soll.
        /// Standardmäßig false (System.Text.Json).
        /// </summary>
        public bool UseNewtonsoft { get; set; } = true;

        public override void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is ObjectResult objectResult)
            {
                if (UseNewtonsoft)
                {
                    // Verwenden von Newtonsoft.Json zur Serialisierung
                    var settings = new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                        Formatting = Newtonsoft.Json.Formatting.Indented // Optional, für lesbare Ausgabe
                    };

                    string json = JsonConvert.SerializeObject(objectResult.Value, settings);
                    context.Result = new ContentResult
                    {
                        Content = json,
                        ContentType = "application/json",
                        StatusCode = objectResult.StatusCode
                    };
                }
                else
                {
                    // Verwenden von System.Text.Json zur Serialisierung
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true,
                        WriteIndented = true // Optional, für lesbare Ausgabe
                    };

                    context.Result = new JsonResult(objectResult.Value, options)
                    {
                        StatusCode = objectResult.StatusCode
                    };
                }
            }
            base.OnResultExecuting(context);
        }
    }

}
