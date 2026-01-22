using System;
using System.IO;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Monitoring.Shared.DTO;
using Monitoring.Shared.DTO.PatchsDto;
using Monitoring.Shared.Enum;
using Monitoring.Shared.Models;
using MonitoringBackend.Data;
using MonitoringBackend.Helper;
using MQTTnet.Protocol;
using SFTPService.Helper;
using static Monitoring.Shared.DTO.PatchsDto.SelectedPatch;

namespace MonitoringBackend.Controllers
{

    [Authorize]
    [ApiController]
    [Route("api/[controller]")]

    public class PatchesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly GetFolderStructure _folderStructure;
        private readonly MQTTHelper _mqtt;


        public PatchesController(AppDbContext db, GetFolderStructure folderStructure, MQTTHelper mqtt)
        {
            _db = db;
            _folderStructure = folderStructure;
            _mqtt = mqtt;

        }





        [HttpGet("getPatchesTypes")]
        public async Task<IActionResult> getPatchesTypes()
        {
            var responseDTO = new APIResponseCoustomizeList<SelectedAnyDropDownOrSamllData, SelectedAnyDropDownOrSamllData> { };
            try
            {

                if (_db != null)
                {
                    //get Server Folder Structure 

                    var AllPatchesType = await _db.PatchTypes
                        .Where(p => p.PatchTypeActiveStatus == 1)
                        .Select(p => new SelectedAnyDropDownOrSamllData
                        {
                            Id = p.PTId,
                            Name = p.PatchTypeName
                        })
                        .ToListAsync();



                    // Get last inserted patch by ID
                    var lastPatch = await _db.NewPatches
                        .OrderByDescending(p => p.PId)
                         .Select(p => new SelectedAnyDropDownOrSamllData
                         {
                             Id = p.PId,
                             Name = p.PatchZipName,
                             Level = p.PatchProcessLevel,
                             Status = p.PatchActiveStatus
                         })
                        .FirstOrDefaultAsync(y => y.Level == 3 && y.Status == 2);


                    responseDTO.Status = true;
                    responseDTO.StatusCode = 2;
                    responseDTO.Message = "operation Success";
                    responseDTO.ValueList = AllPatchesType;
                    responseDTO.Value = lastPatch;

                }

                return Ok(responseDTO);

            }

            catch (Exception ex)
            {
                // Log the exception (optional)
                Console.WriteLine($"Error during Get Server Paths: {ex.Message}");

                // Return a generic error response
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during Get Server Paths";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
                //return StatusCode(500, new { Error = "An unexpected error occurred." });
            }
        }

        [HttpGet("getServerFolderPath")]
        public async Task<IActionResult> getServerPatchFolderPath()
        {
            var responseDTO = new APIResponseSingleValue();
            try
            {

                if (_db != null)
                {
                    //get Server Folder Structure 

                    var path = await _db.ServerFolderPaths
        .Where(p => p.Name == "SR_P")
        .Select(p => p.ServerFolderPathValue)
        .FirstOrDefaultAsync();

                    responseDTO.Status = true;
                    responseDTO.StatusCode = 2;
                    responseDTO.Message = "operation Success";
                    responseDTO.Value = path;

                }

                return Ok(responseDTO);

            }

            catch (Exception ex)
            {
                // Log the exception (optional)
                Console.WriteLine($"Error during Get Server Paths: {ex.Message}");

                // Return a generic error response
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during Get Server Paths";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
                //return StatusCode(500, new { Error = "An unexpected error occurred." });
            }
        }




        [HttpGet("getLastTenPatches")]
        public async Task<IActionResult> getLastTenPatches()
        {
            var responseDTO = new APIResponseOnlyList<SelectedAnyDropDownOrSamllData> { };
            try
            {

                if (_db != null)
                {
                    //get Server Folder Structure 

                    var NewPatches = await _db.NewPatches
         .Where(p => p.PatchProcessLevel == 1 || p.PatchProcessLevel == 2 || p.PatchProcessLevel == 3 || p.PatchProcessLevel == 4)
         .OrderByDescending(p => p.PId)
         .Take(5)
         .Select(p => new SelectedAnyDropDownOrSamllData
         {
             Id = p.PId,
             Name = p.PatchZipName,
             Level = p.PatchProcessLevel,
         })
         .ToListAsync();


                    responseDTO.Status = true;
                    responseDTO.StatusCode = 2;
                    responseDTO.Message = "operation Success";
                    responseDTO.ValueList = NewPatches;

                }

                return Ok(responseDTO);

            }

            catch (Exception ex)
            {
                // Log the exception (optional)
                Console.WriteLine($"Error during Get getLastTenPatches: {ex.Message}");

                // Return a generic error response
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during Get getLastTenPatches";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
                //return StatusCode(500, new { Error = "An unexpected error occurred." });
            }
        }


        [HttpGet("getSelectedPatches")]
        public async Task<IActionResult> getSelectedPatches([FromQuery] int patchtype)
        {
            var responseDTO = new APIResponseOnlyList<SelectedAnyDropDownOrSamllData>();
            try
            {
                var patches = await _db.NewPatches
        .Where(p => p.PatchProcessLevel == 3 && p.PTId == patchtype)
        .OrderByDescending(p => p.PId)
        .Take(20)
        .Select(p => new SelectedAnyDropDownOrSamllData
        {
            Id = p.PId,
            Name = p.PatchZipName
        })
        .ToListAsync();

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Operation success";
                responseDTO.ValueList = patches;

                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error fetching patches";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
            }
        }

        [HttpGet("getSelectedPatchAllDetails")]
        public async Task<IActionResult> getSelectedPatchAllDetails([FromQuery] int patchId)
        {
            var responseDTO = new APIResponseObjectValue<SelectedPatch>();
            try
            {
                var patch = await _db.NewPatches
      .Where(p => p.PatchProcessLevel == 3 && p.PId == patchId)
      .OrderByDescending(p => p.PId)
      .Select(p => new SelectedPatch
      {
          PId = p.PId,
          PatchVersion = p.PatchVersion,
          PatchZipName = p.PatchZipName,
          CreateDate = p.CreateDate,
          Remark = p.Remark,
          PatchZipPath = p.PatchZipPath,
          PatchType = p.PatchType.PatchTypeName
      })
      .FirstOrDefaultAsync();

                if (patch != null)
                {
                    var branchIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (int.TryParse(branchIdClaim, out int branchId))
                    {
                        patch.EnrollBranch = await _db.PatchAssignBranchs
                       .Where(p => p.Id == branchId && p.PId == patchId)
                       .OrderByDescending(p => p.PAB)
                       .Select(p => new SelectedBranchAssingPatch
                       {
                           PAB = p.PAB,
                           BranchId = p.Id,
                           PatchId = p.PId,
                           ProcessLevel = p.ProcessLevel,
                           Status = p.Status,
                           Message = p.Message,
                       })
                       .FirstOrDefaultAsync();
                    }
                }

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Operation success";
                responseDTO.Value = patch;

                return Ok(responseDTO);
            }


            catch (Exception ex)
            {
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error fetching patches";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
            }
        }



        [HttpPost("getPatchesFolderPath")]
        public async Task<IActionResult> getPatchesFolderPath([FromBody] string path)
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
                Console.WriteLine($"Error during Get Server Paths: {ex.Message}");

                // Return a generic error response
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error during Get Server Paths";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
                //return StatusCode(500, new { Error = "An unexpected error occurred." });
            }
        }

        //Client UploadFile Serve Methods Start

        [RequestSizeLimit(long.MaxValue)]
        [HttpPost("DeployPatchServer")]
        public async Task<IActionResult> DeployPatchServer()
        {
            var responseDTO = new APIResponseSingleValue();
            string chunksFolder = string.Empty;
            string patchID = string.Empty;
            string jobUId = string.Empty;

            try
            {
                var form = await Request.ReadFormAsync();

                var file = form.Files["chunk"];
                string fileName = form["fileName"];
                string userId = form["userId"];
                int chunkIndex = int.Parse(form["chunkIndex"]);
                int totalChunks = int.Parse(form["totalChunks"]);
                bool firstChunkStatus = bool.TryParse(form["firstChunkStatus"], out var result) && result;
                jobUId = form["jobUId"];
                string patchversion = form["patchversion"];
                string releaseNote = form["releseNote"];
                string selectedPatchTypeID = form["selectedPatchTypeID"];
                string selectedPatchTypeName = form["selectedPatchTypeName"];

                // Validate file
                if (file == null || file.Length == 0)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Chunk is missing";
                    return BadRequest(responseDTO);

                }

                // Get server chunk base path
                var serverChunkBase = await _db.ServerFolderPaths
                    .Where(p => p.Name == "SR_C")
                    .Select(p => p.ServerFolderPathValue)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(serverChunkBase))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Server chunk path not configured";
                    return BadRequest(responseDTO);

                }

                // Create folder for this job/file
                chunksFolder = Path.Combine(serverChunkBase, $"{jobUId}_{Path.GetFileNameWithoutExtension(fileName)}");
                Directory.CreateDirectory(chunksFolder);

                // Start Job if first chunk
                if (firstChunkStatus)
                {
                    var existingJob = await _db.Jobs.FirstOrDefaultAsync(j => j.JobUId == jobUId);
                    if (existingJob == null)
                    {
                        var newJob = new Job
                        {
                            UserId = userId,
                            JTId = 1,
                            JobUId = jobUId,
                            JobDate = DateTime.Now,
                            JobStartTime = DateTime.Now,
                            JSId = 2,
                            JobMainStatus = 2,
                            JobName = $"Upload file: {fileName} Server",
                            JobMassage = "Start Uploading",
                            JobActive = 1
                        };
                        await _db.Jobs.AddAsync(newJob);
                        await _db.SaveChangesAsync();
                    }
                }

                // Save current chunk
                string chunkPath = Path.Combine(chunksFolder, $"{chunkIndex}.chunk");
                await using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(fs);
                }

                // Count uploaded chunks
                int uploadedChunks = Directory.EnumerateFiles(chunksFolder).Count();

                // If not all chunks yet
                if (uploadedChunks < totalChunks)
                {
                    responseDTO.Status = true;
                    responseDTO.StatusCode = 2;
                    responseDTO.Message = "Chunk";
                    return Ok(responseDTO);
                }

                // All chunks received → create Patch
                string dateString = DateTime.Now.ToString("yyyyMMdd");
                char firstChar = !string.IsNullOrEmpty(selectedPatchTypeName) ? selectedPatchTypeName[0] : '_';
                string zipName = $"{patchversion}_{dateString}_{firstChar}";

                var patch = new NewPatch
                {
                    PatchVersion = patchversion,
                    CreateDate = DateTime.Now,
                    PTId = int.TryParse(selectedPatchTypeID, out int ptIdValue) ? ptIdValue : (int?)null,
                    Remark = releaseNote,
                    PatchActiveStatus = 1,
                    PatchFileName = fileName,
                    PatchFilePath = chunksFolder,
                    PatchProcessLevel = 1,
                    ServerSendChunks = totalChunks,
                    PatchZipName = zipName
                };

                await _db.NewPatches.AddAsync(patch);
                await _db.SaveChangesAsync();
                patchID = patch.PId.ToString();

                // Publish MQTT job
                var topic = "server/mainServer/PATCHPROCESS";
                var jobMessage = new BranchJobRequest<ServerPatch>
                {
                    jobUser = patch.PId.ToString(),
                    jobId = jobUId,
                    jobRqValue = new ServerPatch
                    {
                        ChunksFileName = fileName,
                        ChunksPath = chunksFolder,
                        ZipName = zipName
                    }
                };
                await _mqtt.PublishToServer(jobMessage, topic, MqttQualityOfServiceLevel.ExactlyOnce);

                // Success response
                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Complete";
                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                // Cleanup on failure
                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);

                if (!string.IsNullOrEmpty(jobUId))
                {
                    var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobUId == jobUId);
                    if (job != null)
                    {
                        job.JSId = 4;
                        job.JobActive = 2;
                        job.JobEndTime = DateTime.Now;
                        job.JobMassage = "File upload failed";

                        if (!string.IsNullOrEmpty(patchID))
                        {
                            var patch = await _db.NewPatches.FirstOrDefaultAsync(p => p.PId.ToString() == patchID);
                            if (patch != null)
                            {
                                patch.PatchActiveStatus = 1;
                                patch.PatchProcessLevel = 4;
                                _db.NewPatches.Update(patch);
                            }
                        }

                        _db.Jobs.Update(job);
                        await _db.SaveChangesAsync();
                    }
                }

                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Upload failed";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);

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
                    //job.JobStatus = 3; // Failed
                    job.JobActive = 0;
                    job.JobEndTime = DateTime.Now;
                    job.JobMassage = $"file upload was cancelled by the user";

                    _db.Jobs.Update(job);
                    await _db.SaveChangesAsync();
                }

                // Get server chunk base path
                var serverChunkBase = await _db.ServerFolderPaths
                    .Where(p => p.Name == "SR_C")
                    .Select(p => p.ServerFolderPathValue)
                    .FirstOrDefaultAsync();

                var chunksFolder = Path.Combine(serverChunkBase, $"{jobUId}_{Path.GetFileNameWithoutExtension(fileName)}");

                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);




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



        [HttpPost("PatchdeployBranch")]
        public async Task<IActionResult> PatchdeployBranch(APIRequestObject<SelectedBranchAssingPatch> obj)
        {
            var responseDTO = new APIResponseSingleValue();

            try
            {

                var branchIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (obj == null && obj.ReqValue == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Patch Request is Invalid.";
                    return BadRequest(responseDTO);
                }

                var list = new List<string>() {

                     obj.ReqValue.ExtraValue

                };
                var jobId = new CreateUniqId().GenarateUniqID(list);


                var patch = await _db.NewPatches
     .Where(p => p.PatchProcessLevel == 3 && p.PId == obj.ReqValue.PatchId)
     .OrderByDescending(p => p.PId)
     .Select(p => new SelectedPatch
     {
         PId = p.PId,
         PatchVersion = p.PatchVersion,
         PatchZipName = p.PatchZipName,
         CreateDate = p.CreateDate,
         Remark = p.Remark,
         PatchZipPath = p.PatchZipPath,
         PatchType = p.PatchType.PTId.ToString() ?? "",
         ServerSendChunk = p.ServerSendChunks
     })
     .FirstOrDefaultAsync();

                if (patch != null)
                {




                    if (int.TryParse(branchIdClaim, out int branchId))
                    {


                        PatchAssignBranch? enroll;
                        Job? jobLog;


                        var branch = await _db.Branches
                                                                      .Where(p => p.Id == branchId).
                                                                      Select(t => new
                                                                      {
                                                                          branchCode = t.BranchId,
                                                                          branchId = t.Id,
                                                                          branchName = t.BranchName,
                                                                      })
                                                                      .FirstOrDefaultAsync();

                        var zipPath = $"{patch.PatchZipPath}\\{patch.PatchZipName}.zip";



                        enroll = await _db.PatchAssignBranchs
                                                 .Where(p => p.Id == branchId && p.PId == patch.PId)
                                                 .FirstOrDefaultAsync();

                        var ServerSendChunk = await GetChecksumAsync(zipPath);


                        if (enroll == null)
                        {

                            enroll = new PatchAssignBranch
                            {

                                Id = branchId,
                                PId = patch.PId,
                                Status = PatchStatus.INIT,
                                ProcessLevel = PatchStep.START,
                                StartTime = DateTime.Now,
                                JobUId = jobId,
                                SendChunksBranch = ServerSendChunk,
                                AttemptSteps = 0
                            };


                            _db.PatchAssignBranchs.Add(enroll);



                        }
                        else
                        {
                            jobLog = await _db.Jobs
                                               .Where(p => p.JobUId == enroll.JobUId)
                                               .FirstOrDefaultAsync();

                            jobLog.JobActive = 1;
                            jobLog.JSId = 1;
                            if (enroll.AttemptSteps >= 3)
                            {
                                enroll.Status = PatchStatus.INIT;
                                enroll.ProcessLevel = PatchStep.START;
                                enroll.SendChunksBranch = ServerSendChunk;

                            }
                            else
                            {
                                enroll.Status = PatchStatus.RESTART;
                                enroll.ProcessLevel = PatchStep.RESTART;
                            }

                            enroll.JobUId = jobId;
                            enroll.AttemptSteps++;


                        }

                        jobLog = new Job
                        {
                            UserId = obj.ReqValue.ExtraValue,
                            JTId = 2,
                            JobUId = jobId,
                            JobMainStatus = 1,
                            JobDate = DateTime.Now,
                            JobStartTime = DateTime.Now,
                            JSId = 1,
                            JobName = $"New patch update '{patch.PatchZipName}' for {branch.branchName} ({branch.branchCode})",
                            JobMassage = "Patch update process started.",
                            JobActive = 1
                        };

                        _db.Jobs.Add(jobLog);
                        await _db.SaveChangesAsync();


                        await _db.jobAssignBranches.AddAsync(new JobAssignBranch
                        {
                            Id = branch.branchId,
                            JId = jobLog.JId,
                            IsPatch = true,
                            ProcessLevel = 1,
                        });

                        await _db.SaveChangesAsync();



                        var payload = new PatchDeploymentMqttRequest
                        {
                            UserId = obj.ReqValue.ExtraValue,
                            PatchId = patch.PId.ToString() ?? "",
                            PatchZipPath = zipPath,
                            ExpectedChecksum = enroll.SendChunksBranch?.ToString() ?? "",
                            Status = enroll.Status,
                            PatchRequestType = PatchRequestType.SINGLE_BRANCH_PATCH,
                            Step = enroll.ProcessLevel,
                        };

                        if (patch.PatchType.Equals("1"))
                        {
                            await _mqtt.PublishToServer(
                            payload,
                            $"branch/{branch.branchCode}/PATCH/Application",
                            MqttQualityOfServiceLevel.ExactlyOnce
                        );

                        }


                        responseDTO.Status = true;
                        responseDTO.StatusCode = 2;
                        responseDTO.Message = "operation Success";
                        return Ok(responseDTO);

                    }
                    else
                    {
                        responseDTO.Status = false;
                        responseDTO.StatusCode = 1;
                        responseDTO.Message = "Branch is not Found.";
                        return BadRequest(responseDTO);
                    }
                }
                else
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Patch is not Found.";
                    return BadRequest(responseDTO);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new APIResponseObjectValue<object>
                {
                    Status = false,
                    StatusCode = 0,
                    Message = "Patch deployment failed",
                    Value = null
                });
            }
        }

        private async Task<string> GetChecksumAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = System.IO.File.OpenRead(filePath);

            var hash = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }


        [HttpGet("getSelectedPatchAllDetailsForAllBranch")]
        public async Task<IActionResult> getSelectedPatchAllDetailsForAllBranch([FromQuery] int patchId)
        {
            var responseDTO = new APIResponseObjectValue<SelectedPatch>();
            try
            {
                var patch = await _db.NewPatches
                    .Where(p => p.PatchProcessLevel == 3 && p.PId == patchId)
                    .OrderByDescending(p => p.PId)
                    .Select(p => new SelectedPatch
                    {
                        PId = p.PId,
                        PatchVersion = p.PatchVersion,
                        PatchZipName = p.PatchZipName,
                        CreateDate = p.CreateDate,
                        Remark = p.Remark,
                        PatchZipPath = p.PatchZipPath,
                        PatchType = p.PatchType.PatchTypeName
                    })
                    .FirstOrDefaultAsync();

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = "Operation success";
                responseDTO.Value = patch;

                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Error fetching patch";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
            }
        }


        [HttpPost("PatchdeployAllBranches")]
        public async Task<IActionResult> PatchdeployAllBranches(APIRequestObject<AllBranchPatchRequest> obj)
        {
            var responseDTO = new APIResponseObjectValue<AllBranchPatchResponse>();

            try
            {
                // Validate request
                if (obj?.ReqValue == null || obj.ReqValue.BranchIds == null || !obj.ReqValue.BranchIds.Any())
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Invalid patch request. Branch IDs are required.";
                    return BadRequest(responseDTO);
                }

                // Get user ID from token or request
                var userIdFromToken = User.FindFirstValue(ClaimTypes.Email);
                var userId = !string.IsNullOrEmpty(userIdFromToken) ? userIdFromToken : obj.ReqValue.UserId;

                if (string.IsNullOrEmpty(userId))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "User ID is required.";
                    return BadRequest(responseDTO);
                }

                // Generate main job ID
                var jobId = new CreateUniqId().GenarateUniqID(new List<string> { userId, DateTime.Now.Ticks.ToString() });

                // Get patch details
                var patch = await _db.NewPatches
                    .Where(p => p.PatchProcessLevel == 3 && p.PId == obj.ReqValue.PatchId)
                    .Select(p => new
                    {
                        p.PId,
                        p.PatchVersion,
                        p.PatchZipName,
                        p.PatchZipPath,
                        PTId = p.PTId,
                        p.ServerSendChunks
                    })
                    .FirstOrDefaultAsync();

                if (patch == null)
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "Patch not found or not ready for deployment.";
                    return BadRequest(responseDTO);
                }

                // Get all selected branches that are online
                var branches = await _db.Branches
                    .Where(b => obj.ReqValue.BranchIds.Contains(b.Id))
                    .Where(b => b.TerminalActiveStatus == TerminalActive.TERMINAL_ONLINE)
                    .Select(b => new
                    {
                        b.Id,
                        b.BranchId,
                        b.BranchName,
                        b.TerminalId,
                        b.Location
                    })
                    .ToListAsync();

                if (!branches.Any())
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = "No active branches found for deployment.";
                    return BadRequest(responseDTO);
                }

                // Calculate checksum for the patch
                var zipPath = Path.Combine(patch.PatchZipPath, $"{patch.PatchZipName}.zip");

                if (!System.IO.File.Exists(zipPath))
                {
                    responseDTO.Status = false;
                    responseDTO.StatusCode = 1;
                    responseDTO.Message = $"Patch file not found at: {zipPath}";
                    return BadRequest(responseDTO);
                }

                var checksum = await GetChecksumAsync(zipPath);

                // Create main job log
                var mainJob = new Job
                {
                    UserId = userId,
                    JTId = 2, // Patch deployment job type
                    JobUId = jobId,
                    JobMainStatus = 1,
                    JobDate = DateTime.Now,
                    JobStartTime = DateTime.Now,
                    JSId = 1, // Started status
                    JobName = $"Multi-branch patch deployment: '{patch.PatchZipName}' to {branches.Count} branch(es)",
                    JobMassage = "Patch deployment started for multiple branches.",
                    JobActive = 1
                };

                _db.Jobs.Add(mainJob);
                await _db.SaveChangesAsync();

                // Track deployment results
                var deploymentResults = new List<BranchDeploymentResult>();
                var successCount = 0;
                var failedCount = 0;
                var skippedCount = 0;

                // Deploy to each branch
                foreach (var branch in branches)
                {
                    try
                    {
                        // Generate unique job ID for this branch
                        var branchJobId = new CreateUniqId().GenarateUniqID(new List<string> { jobId, branch.Id.ToString() });

                        // Check if branch already has this patch successfully deployed
                        var existingEnrollment = await _db.PatchAssignBranchs
                            .Where(p => p.Id == branch.Id && p.PId == patch.PId)
                            .OrderByDescending(p => p.PAB)
                            .FirstOrDefaultAsync();

                        PatchAssignBranch enrollment;

                        // Skip if already successfully deployed
                        if (existingEnrollment != null && existingEnrollment.Status == PatchStatus.SUCCESS)
                        {
                            deploymentResults.Add(new BranchDeploymentResult
                            {
                                BranchId = branch.Id,
                                BranchCode = branch.BranchId,
                                BranchName = branch.BranchName,
                                Status = "Skipped",
                                Message = "Branch already has this patch successfully deployed"
                            });
                            skippedCount++;
                            continue;
                        }

                        // Create or update enrollment
                        if (existingEnrollment == null)
                        {
                            enrollment = new PatchAssignBranch
                            {
                                Id = branch.Id,
                                PId = patch.PId,
                                Status = PatchStatus.INIT,
                                ProcessLevel = PatchStep.START,
                                StartTime = DateTime.Now,
                                JobUId = branchJobId,
                                SendChunksBranch = checksum,
                                AttemptSteps = 0,
                                Message = "Deployment initialized"
                            };
                            _db.PatchAssignBranchs.Add(enrollment);
                        }
                        else
                        {
                            // Retry deployment
                            existingEnrollment.Status = existingEnrollment.AttemptSteps >= 3
                                ? PatchStatus.INIT
                                : PatchStatus.RESTART;
                            existingEnrollment.ProcessLevel = existingEnrollment.AttemptSteps >= 3
                                ? PatchStep.START
                                : PatchStep.RESTART;
                            existingEnrollment.JobUId = branchJobId;
                            existingEnrollment.SendChunksBranch = checksum;
                            existingEnrollment.StartTime = DateTime.Now;
                            existingEnrollment.AttemptSteps++;
                            existingEnrollment.Message = existingEnrollment.AttemptSteps >= 3
                                ? "Deployment restarted from beginning"
                                : "Deployment resumed";

                            enrollment = existingEnrollment;
                        }

                        // Create job assignment for this branch
                        var jobAssignment = new JobAssignBranch
                        {
                            Id = branch.Id,
                            JId = mainJob.JId,
                            IsPatch = true,
                            ProcessLevel = 1
                        };
                        _db.jobAssignBranches.Add(jobAssignment);

                        await _db.SaveChangesAsync();

                        // Prepare MQTT payload
                        var payload = new PatchDeploymentMqttRequest
                        {
                            UserId = userId,
                            PatchId = patch.PId.ToString(),
                            PatchZipPath = zipPath,
                            ExpectedChecksum = checksum,
                            Status = enrollment.Status,
                            PatchRequestType = PatchRequestType.ALL_BRANCH_PATCH,
                            Step = enrollment.ProcessLevel,
                            JobId = branchJobId
                        };

                        // Publish to MQTT based on patch type
                        var topic = $"branch/{branch.TerminalId}/PATCH/Application";

                        if (patch.PTId == 1) // Application patch
                        {
                            await _mqtt.PublishToServer(
                                payload,
                                topic,
                                MqttQualityOfServiceLevel.ExactlyOnce
                            );
                        }
                        else
                        {
                            // Handle other patch types if needed
                            Console.WriteLine($"Patch type {patch.PTId} deployment not yet implemented");
                        }

                        // Add successful result
                        deploymentResults.Add(new BranchDeploymentResult
                        {
                            BranchId = branch.Id,
                            BranchCode = branch.BranchId,
                            BranchName = branch.BranchName,
                            Status = "Initiated",
                            Message = "Patch deployment request sent successfully"
                        });

                        successCount++;

                        // Log success
                        Console.WriteLine($"✓ Deployed patch to branch: {branch.BranchName} ({branch.BranchId})");
                    }
                    catch (Exception branchEx)
                    {
                        // Log branch-specific failure
                        Console.WriteLine($"✗ Failed to deploy to branch {branch.BranchId}: {branchEx.Message}");

                        deploymentResults.Add(new BranchDeploymentResult
                        {
                            BranchId = branch.Id,
                            BranchCode = branch.BranchId,
                            BranchName = branch.BranchName,
                            Status = "Failed",
                            Message = $"Deployment initiation failed: {branchEx.Message}"
                        });

                        failedCount++;
                    }
                }

                // Update main job with summary
                mainJob.JobMassage = $"Deployment initiated - Success: {successCount}, Failed: {failedCount}, Skipped: {skippedCount}";

                if (failedCount > 0 && successCount == 0)
                {
                    mainJob.JSId = 4; // All failed
                    mainJob.JobMainStatus = 2; // Error
                    mainJob.JobActive = 0;
                    mainJob.JobEndTime = DateTime.Now;
                }
                else if (successCount > 0)
                {
                    mainJob.JSId = 2; // In progress
                    mainJob.JobMainStatus = 1; // Running
                }

                await _db.SaveChangesAsync();

                // Prepare response
                var totalRequested = obj.ReqValue.BranchIds.Count;
                var offlineCount = totalRequested - branches.Count;

                responseDTO.Status = true;
                responseDTO.StatusCode = 2;
                responseDTO.Message = successCount > 0
                    ? "Multi-branch deployment initiated successfully"
                    : "Deployment failed for all branches";

                responseDTO.Value = new AllBranchPatchResponse
                {
                    JobId = jobId,
                    PatchId = patch.PId,
                    PatchName = patch.PatchZipName,
                    PatchVersion = patch.PatchVersion,
                    TotalBranches = branches.Count,
                    TotalRequested = totalRequested,
                    OfflineBranches = offlineCount,
                    SuccessfulInitiations = successCount,
                    FailedInitiations = failedCount,
                    SkippedInitiations = skippedCount,
                    DeploymentResults = deploymentResults
                };

                return Ok(responseDTO);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Multi-branch deployment error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                responseDTO.Status = false;
                responseDTO.StatusCode = 0;
                responseDTO.Message = "Multi-branch patch deployment failed";
                responseDTO.Ex = ex.Message;
                return StatusCode(StatusCodes.Status500InternalServerError, responseDTO);
            }
        }
        public class AllBranchPatchResponse
        {
            public string JobId { get; set; } = string.Empty;
            public int PatchId { get; set; }
            public string PatchName { get; set; } = string.Empty;
            public string PatchVersion { get; set; } = string.Empty;
            public int TotalBranches { get; set; }
            public int TotalRequested { get; set; }
            public int OfflineBranches { get; set; }
            public int SuccessfulInitiations { get; set; }
            public int FailedInitiations { get; set; }
            public int SkippedInitiations { get; set; }
            public List<BranchDeploymentResult> DeploymentResults { get; set; } = new List<BranchDeploymentResult>();
        }
        // Deployment result per branch
        public class BranchDeploymentResult
        {
            public int BranchId { get; set; }
            public string BranchCode { get; set; } = string.Empty;
            public string BranchName { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty; // Initiated, Failed, Skipped
            public string Message { get; set; } = string.Empty;
        }

        public class PatchDeploymentMqttRequest
        {
            public string UserId { get; set; } = string.Empty;
            public string PatchId { get; set; } = string.Empty;
            public string PatchZipPath { get; set; } = string.Empty;
            public string ExpectedChecksum { get; set; } = string.Empty;
            public PatchStatus? Status { get; set; }
            public PatchRequestType? PatchRequestType { get; set; }
            public PatchStep? Step { get; set; }
            public string JobId { get; set; } = string.Empty;
        }

        public class AllBranchPatchRequest
        {
            public int PatchId { get; set; }
            public List<int> BranchIds { get; set; } = new List<int>();
            public string UserId { get; set; } = string.Empty;
        }

    }


}
