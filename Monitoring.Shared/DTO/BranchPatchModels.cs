using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO
{
    public class PatchDeploymentRequest
    {
        public string JobId { get; set; }
        public string PatchId { get; set; }
        public string PatchZipPath { get; set; }
        public string ExpectedChecksum { get; set; }
    }

    public enum PatchStatus
    {
        INIT,
        IN_PROGRESS,
        SUCCESS,
        FAILED,
        ROLLBACK
    }

    public enum PatchStep
    {
        START,
        DOWNLOAD,
        VALIDATE,
        EXTRACT,
        STOP_APP,
        BACKUP,
        UPDATE,
        START_APP,
        VERIFY,
        CLEANUP,
        ROLLBACK,
        COMPLETE,
        ERROR
    }

    public class PatchStatusUpdate
    {
        public string JobId { get; set; }
        public string BranchId { get; set; }
        public PatchStatus Status { get; set; }
        public PatchStep Step { get; set; }
        public string Message { get; set; }
        public int Progress { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
