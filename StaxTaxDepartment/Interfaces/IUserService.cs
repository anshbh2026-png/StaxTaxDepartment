using StaxTaxDepartment.Models;

namespace StaxTaxDepartment.Interfaces
{
    public interface IUserService
    {
        Task<ApiResponse<UserMobileResponse>> AuthenticateCredentialsAsync(LoginRequest request);
        Task<ApiResponse<string>> VerifyOtpAndGenerateTokenAsync(string username, string otp);
        Task<ApiResponse<UserMobileResponse>> ResendOtpAsync(LoginRequest request);
    }
}