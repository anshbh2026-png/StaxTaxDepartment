namespace StaxTaxDepartment.Models
{
    public class UserMobileResponse
    {
        public string MaskedMobile { get; set; } = string.Empty;
        //public string Message { get; set; } = string.Empty;
        public int ExpiresInSeconds { get; set; } 
    }
}