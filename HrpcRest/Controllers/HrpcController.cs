using HrpcCore.Model.Messages.Mex;
using HrpcCore.Services;
using HrpcRest.Attribute;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace HrpcRest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [CamelCaseJsonFilter]
    [Produces("application/json")]
    [EnableRateLimiting("hrpc")]
    [RequestSizeLimit(1_048_576)] // 1 MB max request body
    public class HrpcController(IHrpcCore hrpcService, ILogger<HrpcController> logger) : ControllerBase
    {
        [HttpPost("json")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(HrpcResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> Set([FromBody] HrpcRequest request, CancellationToken ct)
        {
            try
            {
                var result = await hrpcService.InvokeDi(request, HttpContext, ct);
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Request wurde abgebrochen");
                return Problem(title: "Anfrage abgebrochen", statusCode: 499);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fehler beim Verarbeiten eines Hrpc-Requests");
                return Problem(title: "Interner Serverfehler", statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("json/multi")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(IEnumerable<HrpcResponse?>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> SetMany([FromBody] HrpcMultiRequest? request, CancellationToken ct)
        {
            try
            {
                if (request == null)
                {
                    logger.LogWarning("Multi-Request ist null");
                    return BadRequest("Request is empty.");
                }

                if (request.Requests == null || request.Requests.Count == 0)
                {
                    logger.LogWarning("Multi-Request enthält keine Einträge");
                    return BadRequest("Request is empty.");
                }

                var result = await hrpcService.InvokeDi(request.Requests, HttpContext, request.Mode, ct);
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Multi-Request wurde abgebrochen");
                return Problem(title: "Anfrage abgebrochen", statusCode: 499);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fehler beim Verarbeiten eines Multi-Hrpc-Requests");
                return Problem(title: "Interner Serverfehler", statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        [HttpGet("discovery")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetDiscovery()
        {
            try
            {
                var allControllers = hrpcService.GetDiscoveryInfo();
                return Ok(allControllers);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fehler beim Ermitteln der Discovery-Informationen");
                return Problem(title: "Interner Serverfehler", statusCode: StatusCodes.Status500InternalServerError);
            }
        }
    }
}
