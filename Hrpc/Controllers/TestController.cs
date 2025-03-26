using Hrpc.Hrpc;
using HrpcCore.Attributes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Hrpc.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    //[HrpcAuthorise]
    public class TestController(TestService service) : ControllerBase
    {
        [HttpGet("{id}/{greet}")]
        public Task<ActionResult> Get(int id, string greet)
        {
            return Task.FromResult((ActionResult)Ok(service.GetAdresse(id, greet, CancellationToken.None)));
        }
    }
}
