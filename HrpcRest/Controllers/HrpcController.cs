using HrpcCore.Model.Messages;
using HrpcCore.Services;
using HrpcRest.Attribute;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HrpcRest.Controllers
{
    //[Route("api/[controller]")]
    //[ApiController]
    //[CamelCaseJsonFilter]
    //public class HrpcController(IHrpcCore hrpcService) : ControllerBase
    //{

    //    [HttpPost("json")]
    //    public async Task<ActionResult> Set([FromBody] HrpcRequest request, CancellationToken ct)
    //    {
    //        try
    //        {
    //            var result = await hrpcService.InvokeDi(request, HttpContext, ct);
    //            return Ok(result);
    //            // Die ASP.NET Pipeline serialisiert das `HrpcResponse` als JSON
    //        }
    //        catch (Exception ex)
    //        {
    //            // logging
    //            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
    //        }
    //    }

    //    [HttpPost("json/multi")]
    //    public async Task<ActionResult> SetMany([FromBody] HrpcMultiRequest? request, CancellationToken ct)
    //    {
    //        try
    //        {
    //            if (request == null)
    //                return BadRequest("Request is empty.");
    //            if (request.Requests == null)
    //                return BadRequest("Request is empty.");

    //            var result = await hrpcService.InvokeDi(request.Requests, HttpContext, request.Mode, ct);
    //            return Ok(result);
    //            // Hier wird eine Liste von HrpcResponse als JSON zurückgegeben
    //        }
    //        catch (Exception ex)
    //        {
    //            // logging
    //            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
    //        }
    //    }

    //    [HttpGet("discovery")]
    //    public IActionResult GetDiscovery()
    //    {
    //        try
    //        {
    //            var allControllers = hrpcService.GetDiscoveryInfo();
    //            return Ok(allControllers);
    //        }
    //        catch (Exception ex)
    //        {
    //            // Logging kann hier ergänzt werden
    //            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
    //        }
    //    }

    //    /// <summary>
    //    /// Read a stream fully into a byte-array
    //    /// </summary>
    //    /// <param name="input"></param>
    //    /// <returns></returns>
    //    private static byte[] ReadFully(Stream input)
    //    {
    //        byte[] buffer = new byte[16 * 1024];
    //        using MemoryStream ms = new MemoryStream();
    //        int read;
    //        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
    //        {
    //            ms.Write(buffer, 0, read);
    //        }
    //        return ms.ToArray();
    //    }

    //}
}
