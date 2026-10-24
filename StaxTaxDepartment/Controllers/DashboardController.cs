using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StaxTaxDepartment.Models;

namespace StaxTaxDepartment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] 
    public class DashboardController : ControllerBase
    {
        [HttpGet("data")]
        public IActionResult GetDashboardData()
        {
            return Ok(new ApiResponse<object> 
            { 
                Success = true, 
                Message = "Dashboard data retrieved successfully.", 
                Data = new { NoticeCount = 5, PendingApprovals = 12 } 
            });
        }
    }
}