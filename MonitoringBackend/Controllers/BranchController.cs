using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Monitoring.Shared.DTO;
using Monitoring.Shared.DTO.BranchDto;
using Monitoring.Shared.Enum;
using Monitoring.Shared.Models;
using MonitoringBackend.Data;
using MonitoringBackend.DTO;

namespace MonitoringBackend.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BranchController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;


        public BranchController(UserManager<AppUser> userManager, IConfiguration config, AppDbContext db)
        {
            _userManager = userManager;
            _config = config;
            _db = db;
        }



        [HttpGet("getAllBranch")]
        public async Task<IActionResult> getAllBranch([FromQuery] PagedRequest request)
        {

            var responseDTO = new APIResponseCoustomizeList<SelectBranchDto, int> { };

            try
            {
                IQueryable<Branch> query = _db.Branches;

                if (!string.IsNullOrWhiteSpace(request.Search))
                {
                    query = query.Where(x =>
                        x.BranchName.Contains(request.Search) ||
                        x.BranchId.Contains(request.Search) ||
                        x.TerminalId.Contains(request.Search) ||
                        x.Location.Contains(request.Search));
                }

                if (!string.IsNullOrWhiteSpace(request.Status))
                {
                    if (Enum.TryParse<TerminalActive>(request.Status, out var status))
                    {
                        query = query.Where(x => x.TerminalActiveStatus == status);
                    }
                }

                int totalCount = await query.CountAsync();


                var branches = await query
    .OrderBy(x => x.Id)
    .Skip((request.PageNumber - 1) * request.PageSize)
    .Take(request.PageSize)
    .Select(r => new SelectBranchDto
    {
        Id = r.Id,
        BranchId = r.BranchId,
        BranchName = r.BranchName,
        TerminalActiveStatus = r.TerminalActiveStatus,
        Location = r.Location,
        TerminalName = r.TerminalName,
        TerminalId = r.TerminalId,
        TerminalVersion = r.TerminalVersion
    })
    .ToListAsync();



                if (branches == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Any Branches Not Found";
                    return BadRequest(responseDTO);

                }

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Operation Success";
                responseDTO.Value = totalCount;
                responseDTO.ValueList = branches;

                return Ok(responseDTO);





            }
            catch (Exception ex)
            {
                // Log the exception (optional)
                Console.WriteLine($"Error during registration: {ex.Message}");

                // Return a generic error response
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during Get Branches";
                responseDTO.Ex = ex.Message;
                return BadRequest(responseDTO);
            }
        }


        [HttpPost("LoginBranch")]
        public async Task<IActionResult> LoginBranch([FromBody] BranchLoginDto request)
        {
            var responseDTO = new APIResponseSingleValue();

            try
            {
                //Use for this in branch login access by user Ex: only admin can access this api
                //var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (request == null || request.Id == null || string.IsNullOrEmpty(request.TerminalId))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "branchId and branchCode are required.";
                    return BadRequest(responseDTO);
                }
                //var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var branches = await _db.Branches.FindAsync(request.Id);

                if (branches == null || branches.TerminalId != request.TerminalId)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Any Branches Not Found";
                    return BadRequest(responseDTO);

                }


                var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier,  branches.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Sub, branches.TerminalId)
    };

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


                branches.TerminalSessionKey = tokenString;


                await _db.SaveChangesAsync();


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
                responseDTO.Message = "Error during Get Branches";
                responseDTO.Ex = ex.Message;
                return BadRequest(responseDTO);
            }
        }

    }
}
