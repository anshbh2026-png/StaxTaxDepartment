namespace StaxTaxDepartment.Models
{
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty; // Changed to accept plain password

        public string CaptchaId { get; set; } = string.Empty;
        public string CaptchaValue { get; set; } = string.Empty;
    }
}