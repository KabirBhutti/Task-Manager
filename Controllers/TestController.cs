using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace TaskManagerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                Message = "Task Manager API is working!",
                Version = "1.0",
                Timestamp = DateTime.Now
            });
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new
            {
                Status = "Healthy",
                Service = "Task Manager API"
            });
        }

        [HttpGet("public-test")]
        public IActionResult PublicTest()
        {
            return Ok(new { 
                Message = "This is public, no auth needed", 
                Time = DateTime.Now 
            });
        }

        [HttpGet("debug-auth")]
        [Authorize]
        public IActionResult DebugAuth()
        {
            var user = HttpContext.User;
            var claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList();
            
            return Ok(new
            {
                IsAuthenticated = user.Identity?.IsAuthenticated,
                UserName = user.Identity?.Name,
                Claims = claims,
                HasRoleClaim = user.HasClaim(c => c.Type == ClaimTypes.Role),
                Role = user.FindFirst(ClaimTypes.Role)?.Value
            });
        }
    }
}