using WaevaRpc.Api;
using WaevaRpcCore.Attributes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace WaevaRpc.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    //[WaevaRpcAuthorise]
    public class TestController(TestService service) : ControllerBase
    {
        [HttpGet("{id}/{greet}")]
        public Task<ActionResult> Get(int id, string greet)
        {
            return Task.FromResult((ActionResult)Ok(service.GetAdresse(id, greet, CancellationToken.None)));
        }
    }
}
