namespace StaxTaxDepartment.Models
{
    public class UserProfileResponse
    {
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public decimal Mobile { get; set; } 
        public string ProfileImageUrl { get; set; } = "images/img.png";
    }
}