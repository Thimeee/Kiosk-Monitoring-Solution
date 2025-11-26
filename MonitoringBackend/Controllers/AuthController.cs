using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Monitoring.Shared.DTO;
using MonitoringBackend.Data;
using MonitoringBackend.DTO;

namespace MonitoringBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IConfiguration _config;


        public AuthController(UserManager<AppUser> userManager, IConfiguration config)
        {
            _userManager = userManager;
            _config = config;
        }


        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var user = new AppUser { UserName = dto.Username, Email = dto.Email };
            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            // Assign a default role
            await _userManager.AddToRoleAsync(user, "Admin");

            return Ok("User created with role BranchManager");
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var responseDTO = new APIResponseSingleValue();
            try
            {

                if (dto == null || string.IsNullOrEmpty(dto.Username) || string.IsNullOrEmpty(dto.Password))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Username and password are required.";
                    return BadRequest(responseDTO);
                }

                var user = await _userManager.FindByNameAsync(dto.Username);
                if (user == null || !await _userManager.CheckPasswordAsync(user, dto.Password))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Invalid Username or password";
                    return Unauthorized(responseDTO);
                }

                var userRoles = await _userManager.GetRolesAsync(user);


                var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
        new Claim(ClaimTypes.NameIdentifier, user.Id)
    };

                foreach (var role in userRoles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(3), // token valid for 3 hours
            signingCredentials: creds
        );

                var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "operation Success";
                responseDTO.Value = tokenString;

                return Ok(responseDTO);

            }

            catch (Exception ex)
            {
                // Log the exception (optional)
                Console.WriteLine($"Error during registration: {ex.Message}");

                // Return a generic error response
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during registration";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);

                //return StatusCode(500, new { Error = "An unexpected error occurred." });
            }
        }
    }


}
