using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Monitoring.Shared.DTO;
using Monitoring.Shared.DTO.UserMangment;
using MonitoringBackend.Data;
using MonitoringBackend.Helper;
using SFTPService.Helper;

namespace MonitoringBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UserMangmentController : ControllerBase
    {

        private readonly AppDbContext _db;
        private readonly RoleManager<IdentityRole> _role;
        private readonly UserManager<AppUser> _user;

        public UserMangmentController(AppDbContext db, RoleManager<IdentityRole> role, UserManager<AppUser> user)
        {
            _db = db;
            _role = role;
            _user = user;
        }


        [HttpPost("AddUser")]
        public async Task<ActionResult> AddUser([FromBody] APIRequestObject<AddUserDto> obj)
        {
            var responseDTO = new APIResponseSingleValue();

            try
            {
                if (obj.ReqValue == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "User data not found.";
                    return BadRequest(responseDTO);
                }

                AddUserDto? userValues = obj.ReqValue;
                if (userValues != null)
                {
                    using var transaction = await _db.Database.BeginTransactionAsync();

                    try
                    {
                        var user = await _user.FindByNameAsync(userValues.UserId);

                        if (user != null)
                        {
                            responseDTO.Status = false;
                            responseDTO.StatusCode = 1;
                            responseDTO.Message = "User already exists";
                            return BadRequest(responseDTO);
                        }

                        user = new AppUser
                        {
                            UserName = userValues.UserId,
                            Email = userValues.Email,
                            EmailConfirmed = true,
                            PhoneNumber = userValues.Mobile,
                            PhoneNumberConfirmed = true,
                        };

                        var createResult = await _user.CreateAsync(user, userValues.Password);

                        if (!createResult.Succeeded)
                        {
                            await transaction.RollbackAsync();
                            responseDTO.Status = false;
                            responseDTO.StatusCode = 1;
                            responseDTO.Message = string.Join(", ", createResult.Errors.Select(e => e.Description));
                            return BadRequest(responseDTO);
                        }

                        var roleResult = await _user.AddToRolesAsync(user, userValues.Roles);

                        if (!roleResult.Succeeded)
                        {
                            await transaction.RollbackAsync();
                            responseDTO.Status = false;
                            responseDTO.StatusCode = 1;
                            responseDTO.Message = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                            return BadRequest(responseDTO);
                        }


                        //int rowsAffected = await _db.SaveChangesAsync();

                        //if (rowsAffected <= 0)
                        //{
                        //    await transaction.RollbackAsync();
                        //    responseDTO.Status = false;
                        //    responseDTO.StatusCode = 0;
                        //    responseDTO.Message = "Failed to save user profile";
                        //    return Ok(responseDTO);
                        //}

                        await transaction.CommitAsync();

                        responseDTO.Status = true;
                        responseDTO.StatusCode = 2;
                        responseDTO.Message = "User added successfully";
                        responseDTO.Value = user.Id;

                        return Ok(responseDTO);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        responseDTO.Status = false;
                        responseDTO.StatusCode = 0;
                        responseDTO.Message = ex.Message;
                        return StatusCode(500, responseDTO);
                    }
                }




                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = ex.Message;
                return StatusCode(500, responseDTO);
            }
        }
    }
}
