using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monitoring.Shared.DTO;
using MonitoringBackend.Data;
using MonitoringBackend.Helper;
using MQTTnet.Protocol;
using SFTPService.Helper;

namespace MonitoringBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly MQTTHelper _mqtt;


        public HealthController(AppDbContext db, MQTTHelper mqtt)
        {
            _db = db;
            _mqtt = mqtt;

        }

        [HttpPost("getliveHealth")]
        public async Task<IActionResult> getliveHealth([FromBody] string branch)
        {
            var responseDTO = new APIResponseSingleValue();
            try
            {

                if (branch == null || string.IsNullOrEmpty(branch))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Branch ID required.";
                    return BadRequest(responseDTO);
                }

                var topic = $"branch/{branch}/HEALTH/PerformanceReq";

                var job = new BranchJobRequestFast()
                {
                    jobStartTime = DateTime.Now,

                };
                await _mqtt.PublishToServer(job, topic, MqttQualityOfServiceLevel.AtMostOnce);
                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "operation Success";

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
