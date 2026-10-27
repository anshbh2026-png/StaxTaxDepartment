using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using StaxTaxDepartment.Interfaces;
using StaxTaxDepartment.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace StaxTaxDepartment.Services
{
    public class UserService : IUserService
    {
        private readonly string _connectionString;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;

        public UserService(IConfiguration configuration, IMemoryCache cache)
        {
            _config = configuration;
            _connectionString = _config.GetConnectionString("Default")!;
            _cache = cache;
        }

        // Phase 1: Validate Username/Password & Send OTP
        public async Task<ApiResponse<UserMobileResponse>> AuthenticateCredentialsAsync(LoginRequest request)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                string userPrefix = request.Username.Length >= 2 ? request.Username.Substring(0, 2) : "";

                string? storedPassword = null;
                string mobileNumber = "9999999999"; 

                if (userPrefix == "05")
                {
                    var dealer = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT * FROM buss_detail_master WHERE tin_no = @Id", new { Id = request.Username });
                    if (dealer != null) storedPassword = dealer.password;
                }
                else if (userPrefix == "06")
                {
                    var tdan = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT * FROM tdan_form1_details WHERE tdan_no = @Id AND tdan_status=1", new { Id = request.Username });
                    if (tdan != null) storedPassword = tdan.password;
                }
                else
                {
                    var officerQuery = @"SELECT l.password, 
                                                COALESCE(l.mobile_no, '9999999999') as mobile 
                                         FROM login l WITH(NOLOCK) 
                                         WHERE l.login_id = @Id";
                    var officer = await connection.QueryFirstOrDefaultAsync<dynamic>(officerQuery, new { Id = request.Username });
                    if (officer != null)
                    {
                        storedPassword = officer.password;
                        mobileNumber = officer.mobile ?? "9999999999";
                    }
                }

                if (string.IsNullOrEmpty(storedPassword))
                    return new ApiResponse<UserMobileResponse> { Success = false, Message = "Invalid username or password. Please try again." };

                if (!VerifyLegacyPassword(request.Password, storedPassword))
                {
                    return new ApiResponse<UserMobileResponse> { Success = false, Message = "Invalid username or password. Please try again." };
                }

                string otp = new Random().Next(100000, 999999).ToString();
                int expirySeconds = 120;
                _cache.Set($"otp_{request.Username}", otp, TimeSpan.FromSeconds(expirySeconds));

                string message2 = $"Greetings of the day, Your OTP is {otp} , State Tax Department, Uttarakhand";

                string templateId = _config["SmsSettings:TemplateId"] ?? "1007160993013941987";

                await SendSmsViaGatewayAsync(mobileNumber, message2, templateId);
                string maskedMobile = mobileNumber.Length >= 4
                    ? new string('*', mobileNumber.Length - 4) + mobileNumber.Substring(mobileNumber.Length - 4)
                    : "******9999";

                return new ApiResponse<UserMobileResponse>
                {
                    Success = true,
                    Message = $"OTP sent successfully to registered mobile {maskedMobile}.",
                    Data = new UserMobileResponse
                    {
                        MaskedMobile = maskedMobile,
                        ExpiresInSeconds = expirySeconds,
                    }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<UserMobileResponse> { Success = false, Message = ex.Message };
            }
        }
        public async Task<ApiResponse<UserMobileResponse>> ResendOtpAsync(LoginRequest request)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                string userPrefix = request.Username.Length >= 2 ? request.Username.Substring(0, 2) : "";

                string mobileNumber = "9999999999"; // Fallback mobile number

                if (userPrefix == "05")
                {
                    var dealer = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT * FROM buss_detail_master WHERE tin_no = @Id", new { Id = request.Username });
                }
                else if (userPrefix == "06")
                {
                    var tdan = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT * FROM tdan_form1_details WHERE tdan_no = @Id AND tdan_status=1", new { Id = request.Username });
                }
                else
                {
                    var officerQuery = @"SELECT COALESCE(l.mobile_no, '9999999999') as mobile 
                                 FROM login l WITH(NOLOCK) 
                                 WHERE l.login_id = @Id";
                    var officer = await connection.QueryFirstOrDefaultAsync<dynamic>(officerQuery, new { Id = request.Username });
                    if (officer != null)
                    {
                        mobileNumber = officer.mobile ?? "9999999999";
                    }
                }

                string otp = new Random().Next(100000, 999999).ToString();
                int expirySeconds = 120;
                _cache.Set($"otp_{request.Username}", otp, TimeSpan.FromSeconds(expirySeconds));

                string messageText = $"Greetings of the day, Your OTP is {otp} , State Tax Department, Uttarakhand";
                string templateId = _config["SmsSettings:TemplateId"] ?? "1007160993013941987";

                await SendSmsViaGatewayAsync(mobileNumber, messageText, templateId);

                string maskedMobile = mobileNumber.Length >= 4
                    ? new string('*', mobileNumber.Length - 4) + mobileNumber.Substring(mobileNumber.Length - 4)
                    : "******9999";

                return new ApiResponse<UserMobileResponse>
                {
                    Success = true,
                    Message = $"New OTP sent successfully to registered mobile {maskedMobile}.",
                    Data = new UserMobileResponse
                    {
                        MaskedMobile = maskedMobile,
                        ExpiresInSeconds = expirySeconds,
                    }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<UserMobileResponse> { Success = false, Message = ex.Message };
            }
        }
        public async Task<ApiResponse<string>> VerifyOtpAndGenerateTokenAsync(string username, string otp)
        {
            try
            {
                if (!_cache.TryGetValue($"otp_{username}", out string? cachedOtp))
                {
                    return new ApiResponse<string> { Success = false, Message = "OTP has expired. Please click Resend OTP." };
                }

                if (cachedOtp != otp)
                {
                    return new ApiResponse<string> { Success = false, Message = "Incorrect OTP. Please check and try again." };
                }

                _cache.Remove($"otp_{username}");

                string userPrefix = username.Length >= 2 ? username.Substring(0, 2) : "";
                string roleType = userPrefix == "05" ? "Dealer" : (userPrefix == "06" ? "TDAN" : "Officer");

                var token = GenerateJwtToken(username, roleType);

                return new ApiResponse<string> { Success = true, Message = "OTP Verified Successfully", Data = token };
            }
            catch (Exception ex)
            {
                return new ApiResponse<string> { Success = false, Message = ex.Message };
            }
        }

        private bool VerifyLegacyPassword(string plainPassword, string storedDbPassword)
        {
            if (storedDbPassword == plainPassword) return true;

            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var inputBytes = Encoding.UTF8.GetBytes(plainPassword);
                var hashBytes = md5.ComputeHash(inputBytes);
                var calculatedHash = Convert.ToHexString(hashBytes).ToLower();

                return calculatedHash == storedDbPassword.ToLower();
            }
        }

        private string GenerateJwtToken(string username, string role)
        {
            var secret = _config["JwtSettings:Secret"];
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret!));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[] {
                new Claim(ClaimTypes.NameIdentifier, username),
                new Claim(ClaimTypes.Role, role)
            };

            var token = new JwtSecurityToken(
                issuer: _config["JwtSettings:Issuer"],
                audience: _config["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(45),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task<bool> SendSmsViaGatewayAsync(string mobileNumber, string message, string templateId)
        {
            if (string.IsNullOrWhiteSpace(mobileNumber) ||
                mobileNumber == "9999999999" ||
                !System.Text.RegularExpressions.Regex.IsMatch(mobileNumber, @"^[6-9]\d{9}$"))
            {
                Console.WriteLine("[SMS] Invalid mobile number.");
                return false;
            }

            string apiKey = _config["SmsSettings:MtalkzApiKey"] ?? "";
            string senderId = _config["SmsSettings:SenderId"] ?? "STaxUK";

            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

                var keyValues = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("apikey", apiKey),
            new KeyValuePair<string, string>("senderid", senderId),
            new KeyValuePair<string, string>("number", mobileNumber),
            new KeyValuePair<string, string>("message", message),
            new KeyValuePair<string, string>("template_id", templateId) 
        };

                using var content = new FormUrlEncodedContent(keyValues);

                using var response = await client.PostAsync("https://msg.mtalkz.com/V2/http-api.php", content);

                string result = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"[MTALKZ] HTTP status: {(int)response.StatusCode}");
                Console.WriteLine($"[MTALKZ] Response: {result}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[MTALKZ] HTTP request failed.");
                    return false;
                }

                using var json = JsonDocument.Parse(result);
                if (json.RootElement.TryGetProperty("status", out var statusElement))
                {
                    string status = statusElement.ToString();
                    if (string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("[MTALKZ] SMS request accepted by gateway.");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MTALKZ] Request failed: {ex.Message}");
                return false;
            }

        }
        public async Task<ApiResponse<UserProfileResponse>> GetUserProfileAsync(string username)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                string userPrefix = username.Length >= 2 ? username.Substring(0, 2) : "";

                string displayName = username;
                string designation = "Tax Officer";
                string gender = "Not Specified";
                string email = "";
                decimal mobile = 0;
                string profileImage = "images/img.png"; 
                string employeeCode = "";
                string officeLocation = "";
                string officerStatus = "";

                if (userPrefix == "05")
                {
                    // Dealer lookup
                    var dealer = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT * FROM buss_detail_master WHERE tin_no = @Id", new { Id = username });
                    if (dealer != null)
                    {
                        displayName = dealer.trade_nam ?? username;
                        email = dealer.email ?? "";
                    }
                }
                else
                {
                    // 1. Fetch login details to get auth_circle and auth_sect
                    var loginInfo = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT officer_status, auth_circle, auth_sect, username, permission FROM login WITH(NOLOCK) WHERE login_id = @Id",
                        new { Id = username });

                    officerStatus = loginInfo?.officer_status?.Trim() ?? "";
                    string circleCode = loginInfo?.auth_circle?.Trim() ?? "";
                    string sectorCode = loginInfo?.auth_sect?.Trim() ?? "";
                    string sessionName = loginInfo?.username?.Trim() ?? username;

                    // 2. Resolve Sector Name from sector_master
                    string sectorName = sectorCode;
                    if (!string.IsNullOrEmpty(sectorCode) && !string.IsNullOrEmpty(circleCode))
                    {
                        var resolvedSector = await connection.QueryFirstOrDefaultAsync<string>(
                            "SELECT officename FROM sector_master WITH(holdlock,rowlock) WHERE officecode = @Sector AND newcode = @Circle",
                            new { Sector = sectorCode, Circle = circleCode });
                        if (!string.IsNullOrEmpty(resolvedSector)) sectorName = resolvedSector;
                    }

                    // 3. Resolve Circle Name from office_codes
                    string circleName = circleCode;
                    if (!string.IsNullOrEmpty(circleCode))
                    {
                        var resolvedCircle = await connection.QueryFirstOrDefaultAsync<string>(
                            "SELECT office_name FROM office_codes WITH(holdlock,rowlock) WHERE New_code = @Circle",
                            new { Circle = circleCode });
                        if (!string.IsNullOrEmpty(resolvedCircle)) circleName = resolvedCircle;
                    }

                    // 4. Exact Gst_Officer_Master lookup matching legacy query
                    var officerQuery = @"SELECT Name, Sector, employeecode, Gender, Email, Mobile, Image 
                                 FROM Gst_Officer_Master 
                                 WHERE sector = @Sec1 OR sector = @Sec2 OR sector = @Sec3";

                    var officer = await connection.QueryFirstOrDefaultAsync<dynamic>(officerQuery, new
                    {
                        Sec1 = sectorName,
                        Sec2 = circleName + sectorName,
                        Sec3 = sessionName
                    });

                    if (officer != null)
                    {
                        displayName = officer.Name ?? sessionName;
                        designation = officer.Designation ?? "Tax Officer";
                        gender = officer.Gender ?? "Not Specified";
                        email = officer.Email ?? "";
                        mobile = officer.Mobile ?? 0;
                        employeeCode = officer.employeecode ?? "";

                        if (officer.Image != null)
                        {
                            byte[] bytes = officer.Image as byte[];
                            if (bytes != null && bytes.Length > 0)
                            {
                                profileImage = "data:image/png;base64," + Convert.ToBase64String(bytes);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(employeeCode))
                    {
                        var transferInfo = await connection.QueryFirstOrDefaultAsync<dynamic>(
                            @"SELECT Designation, OfficeName, * 
                      FROM View_Transfer_EmployeeWorkInfo 
                      WHERE EmployeeCode = @EmpCode AND CAST(todate AS DATE) >= CAST(GETDATE() AS DATE)",
                            new { EmpCode = employeeCode });

                        if (transferInfo != null)
                        {
                            if (transferInfo.Designation != null)
                            {
                                designation = transferInfo.Designation;
                            }
                            if (transferInfo.OfficeName != null)
                            {
                                officeLocation = transferInfo.OfficeName;
                            }
                        }
                    }
                }

                return new ApiResponse<UserProfileResponse>
                {
                    Success = true,
                    Message = "Profile fetched successfully",
                    Data = new UserProfileResponse
                    {
                        EmployeeCode = employeeCode,
                        FullName = displayName,
                        Designation = designation,
                        Gender = gender,
                        Email = email,
                        Mobile = mobile,
                        ProfileImageUrl = profileImage,
                        OfficeName = officeLocation,
                        OfficerStatus = officerStatus
                    }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<UserProfileResponse> { Success = false, Message = ex.Message };
            }
        }
    }
}