using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Models;
using MonitoringBackend.Data;
using MonitoringBackend.Helper;
using MQTTnet.Protocol;
using SFTPService.Helper;

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


        [HttpGet("getPatchesTypes")]
        public async Task<IActionResult> getPatchesTypes()
        {
            var responseDTO = new APIResponseCoustomizeList<PatchType, NewPatch> { };
            try
            {

                if (_db != null)
                {
                    //get Server Folder Structure 

                    var AllPatchesType = await _db.PatchTypes
                        .Where(p => p.PatchTypeActiveStatus == 1)
                        .ToListAsync();

                    // Get last inserted patch by ID
                    var lastPatch = await _db.NewPatches
                        .OrderByDescending(p => p.PId)
                        .FirstOrDefaultAsync(y => y.PatchProcessLevel == 3);


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



        [HttpGet("getLastTenPatches")]
        public async Task<IActionResult> getLastTenPatches()
        {
            var responseDTO = new APIResponseCoustomizeList<NewPatch, NewPatch> { };
            try
            {

                if (_db != null)
                {
                    //get Server Folder Structure 

                    var NewPatches = await _db.NewPatches
         .Where(p => p.PatchProcessLevel == 1 || p.PatchProcessLevel == 2 || p.PatchProcessLevel == 3 || p.PatchProcessLevel == 4)
         .OrderByDescending(p => p.PId)
         .Take(5)
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

        //Client UploadFile Serve Methods Start

        [RequestSizeLimit(long.MaxValue)]
        [HttpPost("DeployPatchServer")]
        public async Task<IActionResult> DeployPatchServer()
        {
            var responseDTO = new APIResponseSingleValue();
            string chunksFolder = string.Empty;
            string mergedFile = string.Empty;
            string zipFile = string.Empty;
            string jobUId = string.Empty;
            string mergedFileFolder = string.Empty;
            string patchID = string.Empty;
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


                if (file == null || file.Length == 0)
                    return BadRequest(new APIResponseSingleValue
                    {
                        Status = false,
                        StatusCode = 1,
                        Message = "Chunk is missing"
                    });

                // Store chunks in temp folder
                chunksFolder = Path.Combine($"C:\\Branches\\MCS\\Patches\\Chunks\\{jobUId}_{fileName}\\Chunks", fileName);
                Directory.CreateDirectory(chunksFolder);

                string chunkPath = Path.Combine(chunksFolder, $"{chunkIndex}.chunk");
                using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await file.CopyToAsync(fs);

                // Start Job for first chunk
                if (firstChunkStatus)
                {
                    var newjob = new Job
                    {
                        UserId = userId,
                        JTId = 1,
                        JobUId = jobUId,
                        JobDate = DateTime.Now,
                        JobStartTime = DateTime.Now,
                        JSId = 2,
                        JobMainStatus = 2,
                        JobName = $"Upload file: {fileName} Server",
                        JobMassage = $"Start Uploading",
                        JobActive = 1
                    };

                    await _db.Jobs.AddAsync(newjob);
                    await _db.SaveChangesAsync();
                }

                // If NOT last chunk -> return "Chunk" status
                if (chunkIndex != totalChunks - 1)
                    return Ok(new APIResponseSingleValue { Status = true, StatusCode = 2, Message = "Chunk" });


                string dateString = DateTime.Now.ToString("yyyyMMdd");

                // Get first character safely
                char firstChar = !string.IsNullOrEmpty(selectedPatchTypeName)
                                 ? selectedPatchTypeName[0]
                                 : '_';

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
                    PatchZipName = zipName,
                };

                await _db.NewPatches.AddAsync(patch);
                await _db.SaveChangesAsync();

                patchID = patch.PId.ToString();

                var topic = $"server/mainServer/PATCHPROCESS";

                var job = new BranchJobRequest<ServerPatch>
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

                await _mqtt.PublishToServer(job, topic, MqttQualityOfServiceLevel.ExactlyOnce);






                //// FINAL CHUNK -> MERGE FILE
                //string PatchesFolder = Path.Combine("C:\\Branches\\Patches\\AllNewPatches");
                //if (!Directory.Exists(PatchesFolder))
                //    Directory.CreateDirectory(PatchesFolder);

                //mergedFileFolder = Path.Combine(PatchesFolder, zipName);

                //if (!Directory.Exists(mergedFileFolder))
                //    Directory.CreateDirectory(mergedFileFolder);

                //var ApplicationFolder = Path.Combine(mergedFileFolder, "Application");
                //var ScriptsFolder = Path.Combine(mergedFileFolder, "Scripts");
                //var ReleaseFolder = Path.Combine(mergedFileFolder, "Release");

                //if (!Directory.Exists(ApplicationFolder))
                //    Directory.CreateDirectory(ApplicationFolder);
                //if (!Directory.Exists(ScriptsFolder))
                //    Directory.CreateDirectory(ScriptsFolder);
                //if (!Directory.Exists(ReleaseFolder))
                //    Directory.CreateDirectory(ReleaseFolder);

                //mergedFile = Path.Combine(ApplicationFolder, fileName);


                //using (var outFs = new FileStream(mergedFile, FileMode.Create, FileAccess.Write, FileShare.None))
                //{
                //    foreach (var chunkFile in Directory.GetFiles(chunksFolder).OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f))))
                //    {
                //        using (var inFs = new FileStream(chunkFile, FileMode.Open, FileAccess.Read))
                //        {
                //            byte[] buffer = new byte[1024 * 1024]; // 1 MB buffer
                //            int read;
                //            while ((read = await inFs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                //            {
                //                await outFs.WriteAsync(buffer, 0, read);
                //            }
                //        }
                //    }

                //}

                ////Create ZIP file
                //zipFile = Path.Combine(PatchesFolder, zipName + ".zip");
                //if (System.IO.File.Exists(zipFile))
                //    System.IO.File.Delete(zipFile);

                //using var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create);

                //foreach (var file1 in Directory.GetFiles(mergedFileFolder, "*", SearchOption.AllDirectories))
                //{
                //    var entryName = Path.GetRelativePath(mergedFileFolder, file1).Replace('\\', '/');
                //    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

                //    using var entryStream = entry.Open();
                //    using var fs = new FileStream(file1, FileMode.Open, FileAccess.Read);
                //    byte[] buffer = new byte[1024 * 1024];
                //    int read;
                //    while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                //    {
                //        await entryStream.WriteAsync(buffer, 0, read);
                //    }
                //}

                //// Cleanup chunks and merged file
                //if (Directory.Exists(chunksFolder))
                //    Directory.Delete(chunksFolder, true);

                //if (Directory.Exists(mergedFileFolder))
                //{
                //    Directory.Delete(mergedFileFolder, true);
                //}

                //// Update Job
                //if (!string.IsNullOrEmpty(jobUId))
                //{
                //    var getjob = await _db.Jobs.FirstOrDefaultAsync(j => j.JobUId == jobUId);
                //    if (getjob != null)
                //    {
                //        getjob.JSId = 3;
                //        getjob.JobActive = 2;
                //        getjob.JobEndTime = DateTime.Now;
                //        getjob.JobMassage = $"File uploaded and zipped successfully";

                //        _db.Jobs.Update(getjob);
                //        await _db.SaveChangesAsync();
                //    }
                //}

                //// ✅ Return ZIP file for download
                //var zipBytes = await System.IO.File.ReadAllBytesAsync(zipFile);
                return Ok(new APIResponseSingleValue
                {
                    Status = true,
                    StatusCode = 2,
                    Message = "Complete"
                });
            }
            catch (Exception ex)
            {
                // Cleanup on failure
                try
                {
                    if (Directory.Exists(chunksFolder))
                        Directory.Delete(chunksFolder, true);

                    if (Directory.Exists(mergedFileFolder))
                    {
                        Directory.Delete(mergedFileFolder, true);
                    }

                    if (System.IO.File.Exists(zipFile))
                        System.IO.File.Delete(zipFile);

                    if (!string.IsNullOrEmpty(jobUId))
                    {
                        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobUId == jobUId);
                        if (job != null)
                        {
                            job.JSId = 4;
                            job.JobActive = 2;
                            job.JobEndTime = DateTime.Now;
                            job.JobMassage = $"File upload failed";

                            if (!string.IsNullOrEmpty(patchID))
                            {
                                var patch = new NewPatch
                                {
                                    PatchActiveStatus = 1,
                                    PatchProcessLevel = 4,
                                };
                                _db.NewPatches.Update(patch);

                            }


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
                    //job.JobStatus = 3; // Failed
                    job.JobActive = 0;
                    job.JobEndTime = DateTime.Now;
                    job.JobMassage = $"file upload was cancelled by the user";

                    _db.Jobs.Update(job);
                    await _db.SaveChangesAsync();
                }

                var chunksFolder = Path.Combine("C:\\Branches\\MCS\\SFTPFolder\\Chunks", fileName);
                if (Directory.Exists(chunksFolder))
                    Directory.Delete(chunksFolder, true);

                string finalFolder = Path.Combine("C:\\Branches\\MCS\\SFTPFolder\\Final", fileName);
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

        private static string CalculateFileChecksum(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = System.IO.File.OpenRead(filePath); // Use fully qualified name to avoid ambiguity
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        [HttpGet("deploy")]
        public async Task<IActionResult> DeployPatchGet([FromQuery] Guid? patchId, [FromQuery] string? branchId)
        {
            try
            {
                var jobId = Guid.NewGuid().ToString();
                string patchZipPath = @"C:\Branches\Appliction\project\appliction.zip";
                string checksum = CalculateFileChecksum(patchZipPath);

                var payload = new PatchDeploymentRequest
                {
                    JobId = jobId,
                    PatchId = patchId?.ToString() ?? "",
                    PatchZipPath = patchZipPath,
                    ExpectedChecksum = checksum
                };

                await _mqtt.PublishToServer(
                    payload,
                    $"branch/{branchId ?? "BRANCH002"}/PATCH/Application",
                    MqttQualityOfServiceLevel.ExactlyOnce
                );

                return Ok(new APIResponseObjectValue<object>
                {
                    Status = true,
                    StatusCode = 2,
                    Message = "Patch deployment initiated",
                    Value = payload
                });
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



        public class PatchDeployRequest
        {
            public int PatchId { get; set; }
            public string BranchId { get; set; } // null = all branches
        }

        //Client UploadFile Serve Methods End


    }


}
