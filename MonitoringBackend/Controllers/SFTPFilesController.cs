using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Models;
using MonitoringBackend.Data;
using MonitoringBackend.DTO;
using MonitoringBackend.Helper;
using MQTTnet.Protocol;
using SFTPService.Helper;
using static System.Net.WebRequestMethods;

namespace MonitoringBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SFTPFilesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly GetFolderStructure _folderStructure;
        private readonly MQTTHelper _mqtt;

        public SFTPFilesController(AppDbContext db, GetFolderStructure folderStructure, MQTTHelper mqtt)
        {
            _db = db;
            _folderStructure = folderStructure;
            _mqtt = mqtt;

        }

        [HttpGet("getSFTPFolderPaths")]
        public async Task<IActionResult> Register()
        {

            var responseDTO = new APIResponseObjectValue<SFTPFolderPath> { };

            try
            {

                var branchIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);



                if (int.TryParse(branchIdClaim, out int branchId))
                {
                    var SFTPFolderObj = await _db.SFTPFolders
                         .Where(b => b.Id == branchId)
                         .FirstOrDefaultAsync();

                    if (SFTPFolderObj == null)
                    {
                        responseDTO.Status = false;
                        responseDTO.StatusCode = 1;
                        responseDTO.Message = "SFTP Folder Paths Not Found";
                        return NotFound(responseDTO);
                    }
                    responseDTO.Status = true;
                    responseDTO.StatusCode = 2;
                    responseDTO.Message = "operaction Success";
                    responseDTO.Value = SFTPFolderObj;



                }
                else
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "BranchId is required";
                    return Unauthorized(responseDTO);
                }
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
                responseDTO.Ex = ex.ToString();
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
            }
        }



        [HttpPost("getSFTPServerStructure")]
        public async Task<IActionResult> getSFTPServerStructure([FromBody] string path)
        {
            var responseDTO = new APIResponseObjectValue<FolderNode> { };
            try
            {

                if (path == null || string.IsNullOrEmpty(path))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Server Path are required.";
                    return BadRequest(responseDTO);
                }

                //get Server Folder Structure 
                var Node = await _folderStructure.GetFolderStructureRootAsync(path);

                if (Node == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Folder Stucher ";
                    return NotFound(responseDTO);
                }



                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "operation Success";
                responseDTO.Value = Node;

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


        [HttpPost("getSFTPBranchStructure")]
        public async Task<IActionResult> getSFTPBranchStructure([FromBody] JsonElement data)
        {
            var responseDTO = new APIResponseObjectValue<FolderNode> { };
            try
            {
                string? path = data.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                string? userId = data.TryGetProperty("userId", out var userIdElement) ? userIdElement.GetString() : null;
                string? branchCode = data.TryGetProperty("branchId", out var branchIdElement) ? branchIdElement.GetString() : null;

                if (string.IsNullOrEmpty(path))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Server Path are required.";
                    return BadRequest(responseDTO);
                }
                var branchIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (branchIdClaim == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Branch ID Invalid";
                    return BadRequest(responseDTO);
                }

                if (int.TryParse(branchIdClaim, out int branchId))
                {
                    var SFTPObj = await _db.SFTPFolders
    .Include(s => s.Branch)           // load the related Branch
    .FirstOrDefaultAsync(s => s.Branch.Id == branchId);

                    if (SFTPObj == null)
                    {
                        responseDTO.Status = false;
                        responseDTO.StatusCode = 1;
                        responseDTO.Message = "Branch Not Found";
                        return NotFound(responseDTO);
                    }
                    //var topicID = $"branch/{branchId}/heartbeat";

                    //var topic = $"branch/{SFTPObj.Branch.BranchId}/SFTP/FolderStucher";
                    var topic = $"branch/{branchCode}/SFTP/FolderStucher";

                    var job = new BranchJobRequest<FileDetails>
                    {
                        jobUser = userId,
                        jobStartTime = DateTime.Now,
                        jobRqValue = new FileDetails
                        {
                            branch = new BranchFile
                            {
                                path = SFTPObj.BranchPath,
                            }
                        }
                    };
                    await _mqtt.PublishToServer(job, topic, MqttQualityOfServiceLevel.AtMostOnce);

                    responseDTO.Status = true;
                    responseDTO.StatusCode = 2;
                    responseDTO.Message = "operation Success";

                }





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



        [HttpPost("downloadAndUploadFileBoth")]
        public async Task<IActionResult> downloadAndUploadFileBoth([FromBody] JsonElement data)
        {
            var responseDTO = new APIResponseSingleValue();
            try
            {
                string? path = data.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                string? file = data.TryGetProperty("file", out var branchCodeElement) ? branchCodeElement.GetString() : null;
                string? userId = data.TryGetProperty("userId", out var userIdElement) ? userIdElement.GetString() : null;
                string? branchCode = data.TryGetProperty("branchId", out var branchIdElement) ? branchIdElement.GetString() : null;
                string? type = data.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

                if (string.IsNullOrEmpty(path))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = " Path are required.";
                    return BadRequest(responseDTO);
                }
                var branchIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (branchIdClaim == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Branch ID Invalid";
                    return BadRequest(responseDTO);
                }

                if (int.TryParse(branchIdClaim, out int branchId))
                {
                    var SFTPObj = await _db.SFTPFolders
    .Include(s => s.Branch)           // load the related Branch
    .FirstOrDefaultAsync(s => s.Branch.Id == branchId);

                    if (SFTPObj == null)
                    {
                        responseDTO.Status = false;
                        responseDTO.StatusCode = 1;
                        responseDTO.Message = "Branch Not Found";
                        return NotFound(responseDTO);
                    }
                    //var topicID = $"branch/{branchId}/heartbeat";

                    //var topic = $"branch/{SFTPObj.Branch.BranchId}/SFTP/FolderStucher";

                    BranchJobRequest<FileDetails>? job = null;
                    string? topic = null;

                    if (type == "branch")
                    {
                        topic = $"branch/{branchCode}/SFTP/Download";

                        job = new BranchJobRequest<FileDetails>
                        {
                            jobUser = userId,
                            jobStartTime = DateTime.Now,
                            jobRqValue = new FileDetails
                            {
                                branch = new BranchFile
                                {
                                    name = file,
                                    path = SFTPObj.BranchPath,
                                },

                                server = new ServerFile
                                {

                                    name = file,
                                    path = path,
                                }


                            }
                        };
                    }
                    else if (type == "server")
                    {
                        topic = $"branch/{branchCode}/SFTP/Upload";

                        job = new BranchJobRequest<FileDetails>
                        {
                            jobUser = userId,
                            jobStartTime = DateTime.Now,
                            jobRqValue = new FileDetails
                            {
                                branch = new BranchFile
                                {
                                    name = file,
                                    path = path,
                                },

                                server = new ServerFile
                                {

                                    name = file,
                                    path = SFTPObj.ServerBranchPath,
                                }
                            }
                        };
                    }
                    if (job != null && topic != null)
                    {
                        await _mqtt.PublishToServer(job, topic, MqttQualityOfServiceLevel.ExactlyOnce);

                        responseDTO.Status = true;
                        responseDTO.StatusCode = 2;
                        responseDTO.Message = "operation Success";
                    }
                    else
                    {
                        responseDTO.Status = false;
                        responseDTO.StatusCode = 1;
                        responseDTO.Message = "Server Error Not Found";
                        return NotFound(responseDTO);
                    }


                }





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
            }
        }



        //Delete to selected file branch and server Both

        [HttpPost("deleteFileBoth")]
        public async Task<IActionResult> deleteFileBoth([FromBody] JsonElement data)
        {
            var responseDTO = new APIResponseSingleValue();
            try
            {
                string? path = data.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                string? userId = data.TryGetProperty("userId", out var userIdElement) ? userIdElement.GetString() : null;
                string? branchCode = data.TryGetProperty("branchId", out var branchIdElement) ? branchIdElement.GetString() : null;

                bool? isServer = null;
                if (data.TryGetProperty("isServer", out var isServerElement))
                {
                    isServer = isServerElement.GetBoolean();
                }


                if (string.IsNullOrEmpty(path))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = " Path are required.";
                    return BadRequest(responseDTO);
                }


                if (isServer != null)
                {
                    if ((bool)isServer)
                    {
                        if (!System.IO.File.Exists(path))
                        {
                            responseDTO.Status = false;
                            responseDTO.StatusCode = 1;
                            responseDTO.Message = "Server File not found";
                            return NotFound(responseDTO);
                        }

                        System.IO.File.Delete(path);


                        responseDTO.Status = true;
                        responseDTO.StatusCode = 2;
                        responseDTO.Message = "operation Success";
                        responseDTO.Value = "server";

                    }
                    else
                    {
                        //var topicID = $"branch/{branchId}/heartbeat";

                        //var topic = $"branch/{SFTPObj.Branch.BranchId}/SFTP/FolderStucher";


                        var topic = $"branch/{branchCode}/SFTP/Delete";

                        var job = new BranchJobRequest<FileDetails>
                        {
                            jobUser = userId,
                            jobStartTime = DateTime.Now,
                            jobRqValue = new FileDetails
                            {
                                branch = new BranchFile
                                {
                                    path = path,
                                }
                            }
                        };


                        await _mqtt.PublishToServer(job, topic, MqttQualityOfServiceLevel.ExactlyOnce);

                        responseDTO.Status = true;
                        responseDTO.StatusCode = 2;
                        responseDTO.Message = "operation Success";


                    }

                }
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
            }





        }


        [HttpPost("chunk")]
        public async Task<IActionResult> UploadChunk()
        {
            var responseDTO = new APIResponseSingleValue();
            string chunksFolder = string.Empty;
            string mergedFile = string.Empty;

            try
            {
                var form = await Request.ReadFormAsync();

                var file = form.Files["chunk"];
                string fileName = form["fileName"];
                int chunkIndex = int.Parse(form["chunkIndex"]);
                int totalChunks = int.Parse(form["totalChunks"]);

                if (file == null || file.Length == 0)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Chunk is missing";
                    return BadRequest(responseDTO);
                }

                // Store chunks in temp folder
                chunksFolder = Path.Combine("C:\\Branches\\SFTPFolder\\Chunks", fileName);
                if (!Directory.Exists(chunksFolder))
                    Directory.CreateDirectory(chunksFolder);

                string chunkPath = Path.Combine(chunksFolder, $"{chunkIndex}.chunk");

                using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                    await file.CopyToAsync(fs);

                // If NOT last chunk -> return OK
                if (chunkIndex != totalChunks - 1)
                {
                    responseDTO.Status = true;
                    responseDTO.StatusCode = 2;
                    responseDTO.Message = "Chunk";
                    return Ok(responseDTO);
                }

                // ⭐ FINAL CHUNK -> MERGE FILE

                string finalFolder = Path.Combine("C:\\Branches\\SFTPFolder\\Final");
                if (!Directory.Exists(finalFolder))
                    Directory.CreateDirectory(finalFolder);

                mergedFile = Path.Combine(finalFolder, fileName);

                using (var output = new FileStream(mergedFile, FileMode.Create))
                {
                    for (int i = 0; i < totalChunks; i++)
                    {
                        string part = Path.Combine(chunksFolder, $"{i}.chunk");

                        if (!System.IO.File.Exists(part))
                            throw new Exception($"Missing chunk {i}");

                        byte[] bytes = await System.IO.File.ReadAllBytesAsync(part);
                        await output.WriteAsync(bytes, 0, bytes.Length);
                    }
                }

                // ⭐ CLEANUP: remove chunk folder
                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "File uploaded & merged successfully";

                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                // --- ROLLBACK ---
                try
                {
                    if (Directory.Exists(chunksFolder))
                        Directory.Delete(chunksFolder, true);

                    if (System.IO.File.Exists(mergedFile))
                        System.IO.File.Delete(mergedFile);
                }
                catch { }

                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Upload failed";
                responseDTO.Ex = ex.Message;

                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
            }
        }

    }
}
