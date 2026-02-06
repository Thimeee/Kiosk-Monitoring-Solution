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
using static Monitoring.Shared.DTO.PatchsDto.SelectedPatch;

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
      TerminalName = r.TerminalName ?? null,
      TerminalId = r.TerminalId ?? null,
      TerminalVersion = r.TerminalVersion ?? null,
      TerminalSeriNumber = r.TerminalSeriNumber ?? null,
      TerminalAddDatetime = r.TerminalAddDatetime
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



        [HttpGet("getAllBranchSelectedPatch")]
        public async Task<IActionResult> GetAllBranchSelectedPatch([FromQuery] RequestNotPage request)
        {
            var responseDTO = new APIResponseOnlyList<SelectBranchDtoWithSelectedPatchDto>();

            try
            {
                IQueryable<Branch> query = _db.Branches.AsNoTracking();

                //  Search
                if (!string.IsNullOrWhiteSpace(request.Search))
                {
                    query = query.Where(x =>
                        x.BranchName.Contains(request.Search) ||
                        x.BranchId.Contains(request.Search) ||
                        x.TerminalId.Contains(request.Search) ||
                        x.Location.Contains(request.Search));
                }

                //  Status filter

                var branches = await query
                    .OrderBy(x => x.Id)
                    .Select(r => new SelectBranchDtoWithSelectedPatchDto
                    {
                        Id = r.Id,
                        BranchId = r.BranchId,
                        BranchName = r.BranchName,
                        TerminalActiveStatus = r.TerminalActiveStatus,
                        Location = r.Location,
                        TerminalId = r.TerminalId
                    })
                    .ToListAsync();

                //  Load patch assignments in ONE query
                if (request.SelectPatch && request.PatchId > 0 && branches.Any())
                {

                    if (!string.IsNullOrWhiteSpace(request.Status) &&
                 Enum.TryParse<TerminalActive>(request.Status, out var status))
                    {
                        query = query.Where(x => x.TerminalActiveStatus == status);
                    }


                    var branchIds = branches.Select(b => b.Id).ToList();

                    var patchAssignments = await _db.PatchAssignBranchs
                        .AsNoTracking()
                        .Where(p => p.PId == request.PatchId && branchIds.Contains(p.Id))
                        .Select(r => new SelectedBranchAssingPatch
                        {
                            PAB = r.PAB,
                            BranchId = r.Id,
                            PatchId = r.PId,
                            ProcessLevel = r.ProcessLevel,
                            Status = r.Status,
                            Message = r.Message
                        })
                        .ToListAsync();


                    //  Attach patch info
                    foreach (var branch in branches)
                    {

                        branch.EnrollBranch = patchAssignments
                            .FirstOrDefault(p => p.BranchId == branch.Id);
                        if (branch.EnrollBranch == null)
                        {
                            branch.SelectPatchNotEnrollPatchStatus = true;
                        }

                    }
                }

                if (!branches.Any())
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "No branches found";
                    return Ok(responseDTO);
                }

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Operation Success";
                responseDTO.ValueList = branches;

                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during Get Branches";
                responseDTO.Ex = ex.Message;
                return StatusCode(500, responseDTO);
            }
        }



        [HttpPost("AddBranch")]
        public async Task<IActionResult> AddBranch([FromBody] APIRequestObject<SelectBranchDto> requestObj)
        {
            var responseDTO = new APIResponseSingleValue();

            try
            {
                if (requestObj?.ReqValue == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Branch data is missing.";
                    return BadRequest(responseDTO);
                }

                var obj = requestObj.ReqValue;

                bool exists = await _db.Branches
                    .AnyAsync(b => b.TerminalId == obj.TerminalId);

                if (exists)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "This terminal already exists.";
                    return Conflict(responseDTO);
                }

                var branch = new Branch
                {
                    TerminalId = obj.TerminalId,
                    BranchId = obj.BranchId,
                    BranchName = obj.BranchName,
                    Location = obj.Location,
                    TerminalActiveStatus = TerminalActive.TERMINAL_NOT_VERIFY,
                    TerminalVersion = "V001",
                    TerminalSeriNumber = "CDK001",
                    TerminalName = "CDK",
                };

                string? basePath = _config["ServerConfig:ServerTerminalsPath"];

                if (string.IsNullOrWhiteSpace(basePath))
                    throw new InvalidOperationException("Server terminals path is missing in configuration.");

                string terminalFolderPath = Path.Combine(basePath, obj.TerminalId);

                Directory.CreateDirectory(terminalFolderPath);
                branch.ServerBranchFolderpath = terminalFolderPath;

                //new SFTPFolderPath
                //{
                //    Id = branch.Id,
                //    ServerBranchPath = terminalFolderPath,
                //    BranchPath = "C:\\MCS",
                //    ServerStatus = 1
                //};


                _db.Branches.Add(branch);
                await _db.SaveChangesAsync();

                _db.SFTPFolders.Add(new SFTPFolderPath
                {
                    Id = branch.Id,
                    ServerBranchPath = terminalFolderPath,
                    BranchPath = "C:\\MCS",
                    ServerStatus = 1
                });
                await _db.SaveChangesAsync();

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Branch added successfully.";

                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "An error occurred while adding the branch.";
                responseDTO.Ex = ex.Message;

                return StatusCode(500, responseDTO);
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
