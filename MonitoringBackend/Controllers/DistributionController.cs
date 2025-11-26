using Microsoft.AspNetCore.Mvc;
using Monitoring.Shared.DTO;
using MonitoringBackend.Helper;

namespace MonitoringBackend.Controllers
{

    [ApiController]
    [Route("api/distribute")]
    public class DistributionController : ControllerBase
    {
        private readonly RabbitHelper _rabbit;
        private readonly SftpStorageService _sftp;


        public DistributionController(RabbitHelper rabbit, SftpStorageService sftp)
        {
            _rabbit = rabbit;
            _sftp = sftp;
        }

        // ✅ 1 Upload → 400 Branches Download
        //[HttpPost("upload-global")]
        //public async Task<IActionResult> UploadGlobal(IFormFile file)
        //{
        //    var path = await _sftp.SaveFileAsync(file);

        //    var job = new BranchJobRequest
        //    {
        //        command = "DownloadGlobalUpdate",
        //        file = new BranchFile
        //        {
        //            name = file.FileName,
        //            path = path,
        //            size = file.Length
        //        }
        //    };

        //    await _rabbit.PublishBroadcast(job);
        //    return Ok(new { message = "Update sent to all branches", file = file.FileName });
        //}


        //[HttpPost("upload-global-to-folder/{branchId}")]
        //public async Task<IActionResult> UploadGlobalto(string branchId)
        //{
        //    try
        //    {
        //        var job = new BranchJobRequest
        //        {
        //            command = "SendToFolderStructure",
        //            file = new BranchFile
        //            {
        //                path = "C:/SFTP_Acess_Folder",
        //            }
        //        };

        //        await _rabbit.PublishToBranch(branchId, job);
        //        return Ok(new { message = "Update sent to all branches" });
        //    }
        //    catch (Exception ex)
        //    {
        //        // log exception
        //        //_logger.LogError(ex, "Error uploading file to branch {branchId}", branchId);
        //        return StatusCode(500, $"Internal Server Error: {ex.Message}");
        //    }
        //}

        // ✅ Send command to exact 1 branch
        //[HttpPost("upload-to/{branchId}")]
        //public async Task<IActionResult> UploadToBranch(string branchId, IFormFile file)
        //{
        //    if (file == null || file.Length == 0)
        //        return BadRequest("File is empty");

        //    try
        //    {
        //        var path = await _sftp.SaveFileAsync(file);

        //        var job = new BranchJobRequest<FileDetails>
        //        {
        //            jobcommand = "DownloadGlobalUpdate",
        //            jobType = "SFTP",
        //            jobRqValue = new FileDetails
        //            {
        //                server = new ServerFile
        //                {
        //                    name = file.FileName,
        //                    path = path,
        //                    size = file.Length
        //                },
        //                branch = new BranchFile
        //                {
        //                    name = file.FileName,
        //                    path = "D:/testSSH",
        //                    size = file.Length
        //                }
        //            }
        //        };
        //        await _rabbit.PublishToBranch(branchId, job);
        //        return Ok(new { message = $"File sent to branch {branchId}" });
        //    }
        //    catch (Exception ex)
        //    {
        //        // log exception
        //        //_logger.LogError(ex, "Error uploading file to branch {branchId}", branchId);
        //        return StatusCode(500, $"Internal Server Error: {ex.Message}");
        //    }
        //}

        // ✅ Ask branch to upload file back to main
        //[HttpPost("request-upload/{branchId}")]
        //public async Task<IActionResult> RequestUpload(string branchId, [FromQuery] string remoteFilePath)
        //{
        //    if (string.IsNullOrWhiteSpace(branchId) || string.IsNullOrWhiteSpace(remoteFilePath))
        //        return BadRequest(new { message = "Branch or remote file path is missing." });

        //    var job = new BranchJobRequest
        //    {
        //        command = "UploadFileToMain",
        //        file = new BranchFile
        //        {
        //            name = Path.GetFileName(remoteFilePath),
        //            path = remoteFilePath
        //        }
        //    };

        //    await _rabbit.PublishToBranch(branchId, job);

        //    return Ok(new { message = $"Requested file upload from branch {branchId}" });
        //}

    }



}
