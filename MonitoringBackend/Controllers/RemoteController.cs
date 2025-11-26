using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Models;
using MonitoringBackend.Data;
using MonitoringBackend.DTO;

namespace MonitoringBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class RemoteController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;


        public RemoteController(UserManager<AppUser> userManager, IConfiguration config, AppDbContext db)
        {
            _userManager = userManager;
            _config = config;
            _db = db;
        }


        [HttpGet("getRemoteDetails")]
        public async Task<IActionResult> RemoteDetails()
        {

            var responseDTO = new APIResponseObjectValue<BranchRemot>();

            try
            {
                var branchIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);



                if (int.TryParse(branchIdClaim, out int branchId))
                {

                    var branchRemote = await _db.Remote
                             .Where(b => b.Id == branchId)
                             .FirstOrDefaultAsync();

                    if (branchRemote != null)
                    {
                        responseDTO.Status = true;
                        responseDTO.StatusCode = 2;
                        responseDTO.Message = "operation Success";
                        responseDTO.Value = branchRemote;
                    }
                    else
                    {
                        responseDTO.Status = false;
                        responseDTO.StatusCode = 1;
                        responseDTO.Message = "Any Remote Details Not Found";
                        return BadRequest(responseDTO);
                    }

                    return Ok(responseDTO);
                }
                else
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "BranchId is required";
                    return BadRequest(responseDTO);
                }
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
