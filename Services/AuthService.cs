using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TaskManagerAPI.Data;
using TaskManagerAPI.DTOs;
using TaskManagerAPI.Helpers;
using TaskManagerAPI.Models;

namespace TaskManagerAPI.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto);
        Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
        Task<AuthResponseDto> RefreshTokenAsync(string token, string refreshToken);
        Task<bool> LogoutAsync(int userId);
        Task<UserDto?> GetUserProfileAsync(int userId);
        Task<List<AdminUserDto>> GetAllUsersAsync(int currentUserId);
        Task<bool> UpdateUserRoleAsync(int userId, string newRole, int currentUserId);
    }

    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // 1. REGISTER USER (Always as "User" role, never "Admin")
        public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
        {
            try
            {
                // Check if passwords match
                if (registerDto.Password != registerDto.ConfirmPassword)
                    throw new Exception("Passwords do not match");

                // Check if email already exists
                if (await _context.Users.AnyAsync(u => u.Email == registerDto.Email))
                    throw new Exception("Email already exists");

                // Check if username already exists
                if (await _context.Users.AnyAsync(u => u.Username == registerDto.Username))
                    throw new Exception("Username already exists");

                // Create new user - ALWAYS as "User" role
                var user = new User
                {
                    Username = registerDto.Username,
                    Email = registerDto.Email,
                    Password = PasswordHelper.HashPassword(registerDto.Password),
                    Role = "User", // Important: Always "User", never "Admin"
                    CreatedAt = DateTime.UtcNow
                };

                // Save to database
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Generate tokens
                var token = GenerateJwtToken(user);
                var refreshToken = GenerateRefreshToken();

                // Save refresh token
                user.RefreshToken = refreshToken;
                user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
                await _context.SaveChangesAsync();

                return new AuthResponseDto
                {
                    Token = token,
                    RefreshToken = refreshToken,
                    TokenExpiry = DateTime.UtcNow.AddMinutes(15),
                    Message = "Registration successful",
                    User = new UserDto
                    {
                        Id = user.Id,
                        Username = user.Username,
                        Email = user.Email,
                        Role = user.Role,
                        CreatedAt = user.CreatedAt
                    }
                };
            }
            catch (Exception ex)
            {
                // Log error for debugging
                Console.WriteLine($"Registration error: {ex.Message}");
                throw new Exception($"Registration failed: {ex.Message}");
            }
        }

        // 2. LOGIN USER (Both Users and Admins can login)
        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            try
            {
                // Find user by email
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginDto.Email);

                if (user == null)
                    throw new Exception("Invalid email or password");

                // Verify password
                if (!PasswordHelper.VerifyPassword(loginDto.Password, user.Password))
                    throw new Exception("Invalid email or password");

                // Update last login time
                user.LastLoginAt = DateTime.UtcNow;

                // Generate tokens
                var token = GenerateJwtToken(user);
                var refreshToken = GenerateRefreshToken();

                // Save refresh token
                user.RefreshToken = refreshToken;
                user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
                await _context.SaveChangesAsync();

                return new AuthResponseDto
                {
                    Token = token,
                    RefreshToken = refreshToken,
                    TokenExpiry = DateTime.UtcNow.AddMinutes(15),
                    Message = $"Login successful. Welcome {(user.Role == "Admin" ? "Admin" : "User")}!",
                    User = new UserDto
                    {
                        Id = user.Id,
                        Username = user.Username,
                        Email = user.Email,
                        Role = user.Role,
                        CreatedAt = user.CreatedAt,
                        LastLoginAt = user.LastLoginAt
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login error: {ex.Message}");
                throw new Exception($"Login failed: {ex.Message}");
            }
        }

        // 3. REFRESH TOKEN (When 15-minute token expires)
        public async Task<AuthResponseDto> RefreshTokenAsync(string token, string refreshToken)
        {
            try
            {
                // Get user info from expired token
                var principal = GetPrincipalFromExpiredToken(token);
                var userId = int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);

                // Find user in database
                var user = await _context.Users.FindAsync(userId);

                // Check if refresh token is valid
                if (user == null || user.RefreshToken != refreshToken || user.RefreshTokenExpiry <= DateTime.UtcNow)
                    throw new Exception("Invalid refresh token");

                // Generate new tokens
                var newToken = GenerateJwtToken(user);
                var newRefreshToken = GenerateRefreshToken();

                // Save new refresh token
                user.RefreshToken = newRefreshToken;
                user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
                await _context.SaveChangesAsync();

                return new AuthResponseDto
                {
                    Token = newToken,
                    RefreshToken = newRefreshToken,
                    TokenExpiry = DateTime.UtcNow.AddMinutes(15),
                    Message = "Token refreshed successfully"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Refresh token error: {ex.Message}");
                throw new Exception($"Token refresh failed: {ex.Message}");
            }
        }

        // 4. LOGOUT (Remove refresh token)
        public async Task<bool> LogoutAsync(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) return false;

                // Clear refresh token
                user.RefreshToken = null;
                user.RefreshTokenExpiry = null;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logout error: {ex.Message}");
                return false;
            }
        }

        // 5. GET USER PROFILE
        public async Task<UserDto?> GetUserProfileAsync(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) return null;

                return new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Get profile error: {ex.Message}");
                return null;
            }
        }

        // 6. ADMIN: GET ALL USERS (Admin only)
        public async Task<List<AdminUserDto>> GetAllUsersAsync(int currentUserId)
        {
            try
            {
                // Check if current user is Admin
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (currentUser?.Role != "Admin")
                    throw new UnauthorizedAccessException("Admin access required");

                return await _context.Users
                    .Include(u => u.Tasks)
                    .Select(u => new AdminUserDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        Role = u.Role,
                        CreatedAt = u.CreatedAt,
                        LastLoginAt = u.LastLoginAt,
                        TaskCount = u.Tasks.Count
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Get all users error: {ex.Message}");
                throw;
            }
        }

        // 7. ADMIN: UPDATE USER ROLE (Admin only)
        public async Task<bool> UpdateUserRoleAsync(int userId, string newRole, int currentUserId)
        {
            try
            {
                // Check if current user is Admin
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (currentUser?.Role != "Admin")
                    throw new UnauthorizedAccessException("Admin access required");

                var userToUpdate = await _context.Users.FindAsync(userId);
                if (userToUpdate == null) return false;

                // Admin cannot change their own role
                if (userToUpdate.Id == currentUserId)
                    throw new Exception("Cannot change your own role");

                // Validate role
                if (newRole != "Admin" && newRole != "User")
                    throw new Exception("Invalid role. Must be 'Admin' or 'User'");

                userToUpdate.Role = newRole;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update role error: {ex.Message}");
                throw;
            }
        }

        // ========== HELPER METHODS ==========

        // Generate JWT token (15 minutes expiry)
        private string GenerateJwtToken(User user)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

            var claims = new[]
            {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = credentials
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        // Generate refresh token
        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        // Get user from expired token
        private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = false, // Accept expired tokens
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!)),
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);

            return principal;
        }
    }
}