using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using StaxTaxDepartment.Interfaces;
using StaxTaxDepartment.Models;
using System.Security.Claims;

namespace StaxTaxDepartment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IMemoryCache _cache;

        public AuthController(IUserService userService, IMemoryCache cache)
        {
            _userService = userService;
            _cache = cache;
        }

        [HttpGet("captcha")]
        public IActionResult GetCaptcha()
        {
            Random random = new Random();
            string captchaText = random.Next(100000, 999999).ToString();
            string captchaId = Guid.NewGuid().ToString();

            _cache.Set(captchaId, captchaText, TimeSpan.FromMinutes(5));

            return Ok(new
            {
                Success = true,
                CaptchaId = captchaId,
                CaptchaImageText = captchaText
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new ApiResponse<string> { Success = false, Message = "Username and Password are required." });

            // 1. Verify Captcha
            if (!_cache.TryGetValue(request.CaptchaId, out string? savedCaptcha))
            {
                return BadRequest(new ApiResponse<string> { Success = false, Message = "Captcha expired. Please refresh." });
            }

            if (savedCaptcha != request.CaptchaValue)
            {
                return BadRequest(new ApiResponse<string> { Success = false, Message = "Invalid Captcha." });
            }

            _cache.Remove(request.CaptchaId);

            // 2. Validate Credentials & Trigger OTP
            var response = await _userService.AuthenticateCredentialsAsync(request);

            if (!response.Success)
                return Unauthorized(response);

            return Ok(response);
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Otp))
                return BadRequest(new ApiResponse<string> { Success = false, Message = "Username and OTP are required." });

            var response = await _userService.VerifyOtpAndGenerateTokenAsync(request.Username, request.Otp);

            if (!response.Success)
                return BadRequest(response);

            return Ok(response);
        }

        [HttpPost("resend-otp")]
        public async Task<IActionResult> ResendOtp([FromBody] LoginRequest request)
        {
            var result = await _userService.ResendOtpAsync(request);
            return Ok(result);
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile([FromQuery] string? username)
        {
            // If username query parameter is empty, fallback to token claims
            if (string.IsNullOrEmpty(username))
            {
                username = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                           ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                           ?? User.FindFirst("sub")?.Value;
            }

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { success = false, message = "Username could not be determined." });
            }

            var result = await _userService.GetUserProfileAsync(username);
            return Ok(result);
        }
    }
}