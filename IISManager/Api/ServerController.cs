namespace Techie.IISManager.Api
{
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// 
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ServerController : ControllerBase
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("servicesstatus")]
        public IActionResult GetServicesStatus()
        {
            return Ok();
        }
    }
}