namespace StaxTaxDepartment.Models
{
    public class OtpVerifyRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
    }
}