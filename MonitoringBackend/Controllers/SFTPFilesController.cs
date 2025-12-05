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
        public async Task<IActionResult> getSFTPFolderPaths()
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


        [RequestSizeLimit(long.MaxValue)]
        [HttpPost("chunk")]
        public async Task<IActionResult> UploadChunk()
        {
            var responseDTO = new APIResponseSingleValue();
            string chunksFolder = string.Empty;
            string mergedFile = string.Empty;
            string jobUId = string.Empty;

            try
            {
                var form = await Request.ReadFormAsync();

                var file = form.Files["chunk"];
                string fileName = form["fileName"];
                string userId = form["userId"];
                string branchId = form["branchId"];
                int chunkIndex = int.Parse(form["chunkIndex"]);
                int totalChunks = int.Parse(form["totalChunks"]);
                bool firstChunkStatus = bool.TryParse(form["firstChunkStatus"], out var result) && result;
                jobUId = form["jobUId"];



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

                using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    await file.CopyToAsync(fs);



                if (firstChunkStatus)
                {
                    int branchIdInt = int.Parse(branchId);

                    var job = new Job
                    {
                        BranchId = branchIdInt,
                        UserId = userId,
                        JTId = 1,
                        JobUId = jobUId,
                        JobDate = DateTime.Now,
                        JobStartTime = DateTime.Now,
                        JobStatus = 1, // In Progress
                        JobMassage = $"Uploading file: {fileName}",
                        JobActive = 1
                    };

                    await _db.Jobs.AddAsync(job);
                    await _db.SaveChangesAsync();
                }


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

                using (var output = new FileStream(mergedFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    for (int i = 0; i < totalChunks; i++)
                    {
                        string part = Path.Combine(chunksFolder, $"{i}.chunk");
                        using (var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                        {
                            await input.CopyToAsync(output);
                        }
                    }
                }

                // ⭐ CLEANUP: remove chunk folder
                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);

                if (jobUId != null)
                {
                    var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobUId == jobUId);
                    job.JobStatus = 2;
                    job.JobActive = 0;
                    job.JobEndTime = DateTime.Now;
                    job.JobMassage = $"File uploaded successfully: {fileName}";

                    _db.Jobs.Update(job);
                    await _db.SaveChangesAsync();
                }

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Complete";

                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(chunksFolder))
                        Directory.Delete(chunksFolder, true);

                    if (System.IO.File.Exists(mergedFile))
                        System.IO.File.Delete(mergedFile);

                    if (!string.IsNullOrEmpty(jobUId))
                    {
                        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobUId == jobUId);
                        if (job != null)
                        {
                            job.JobStatus = 0; // Failed
                            job.JobActive = 0;
                            job.JobEndTime = DateTime.Now;
                            job.JobMassage = $"File upload failed: {ex.Message}";

                            _db.Jobs.Update(job);
                            await _db.SaveChangesAsync();
                        }
                    }
                }
                catch { }

                return StatusCode(StatusCodes.Status500InternalServerError, new APIResponseSingleValue
                {
                    Status = false,
                    StatusCode = 0,
                    Message = "Upload failed",
                    Ex = ex.Message
                });
            }

        }


        [HttpPost("UploadFileToServerCancel")]
        public async Task<IActionResult> UploadFileToServerCancel([FromBody] JsonElement data)
        {
            var responseDTO = new APIResponseSingleValue();
            try
            {
                string? jobUId = data.TryGetProperty("jobUId", out var jobUIdElement) ? jobUIdElement.GetString() : null;
                string? fileName = data.TryGetProperty("fileName", out var fileNameElement) ? fileNameElement.GetString() : null;

                if (jobUId == null || string.IsNullOrEmpty(jobUId))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "JobId are required.";
                    return BadRequest(responseDTO);
                }


                var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobUId == jobUId);
                if (job != null)
                {
                    job.JobStatus = 3; // Failed
                    job.JobActive = 0;
                    job.JobEndTime = DateTime.Now;
                    job.JobMassage = $"file upload was cancelled by the user";

                    _db.Jobs.Update(job);
                    await _db.SaveChangesAsync();
                }

                var chunksFolder = Path.Combine("C:\\Branches\\SFTPFolder\\Chunks", fileName);
                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);

                string finalFolder = Path.Combine("C:\\Branches\\SFTPFolder\\Final", fileName);
                if (Directory.Exists(finalFolder))
                    System.IO.File.Delete(finalFolder);


                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "operation Success";

                return Ok(responseDTO);

            }

            catch (Exception ex)
            {
                // Log the exception (optional)
                Console.WriteLine($"Error during File upload: {ex.Message}");

                // Return a generic error response
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during File upload";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
                //return StatusCode(500, new { Error = "An unexpected error occurred." });
            }
        }

    }
}
