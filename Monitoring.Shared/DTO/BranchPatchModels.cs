using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Monitoring.Shared.Enum;

namespace Monitoring.Shared.DTO
{
    public class PatchDeploymentMqttRequest
    {
        public string UserId { get; set; }
        public PatchRequestType PatchRequestType { get; set; }
        public string PatchId { get; set; }
        public string PatchZipPath { get; set; }
        public string ExpectedChecksum { get; set; }
        public PatchStatus? Status { get; set; }
        public PatchStep? Step { get; set; }

    }
    public class PatchStatusUpdateMqttResponse
    {
        public string UserId { get; set; }

        public string PatchId { get; set; }
        public PatchRequestType PatchRequestType { get; set; }

        public PatchStatus? Status { get; set; }
        public PatchStep? Step { get; set; }
        public string Message { get; set; }
        public int Progress { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
