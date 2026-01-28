using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Monitoring.Shared.DTO.BranchDto;
using Monitoring.Shared.Enum;

namespace Monitoring.Shared.DTO.PatchsDto
{

    public class SelectedPatch
    {
        public int PId { get; set; }
        public string? PatchVersion { get; set; }
        public string? PatchType { get; set; }
        public DateTime? CreateDate { get; set; }

        public string? Remark { get; set; }

        public string? PatchZipName { get; set; }

        public string? PatchZipPath { get; set; }
        public int? ServerSendChunk { get; set; }

        public SelectedBranchAssingPatch? EnrollBranch { get; set; }



        public class SelectedBranchAssingPatch
        {
            public long PAB { get; set; }
            public int? BranchId { get; set; }
            public int? PatchId { get; set; }
            public PatchStep? ProcessLevel { get; set; }
            public PatchStatus? Status { get; set; }
            public string? Message { get; set; }
            public string? ExtraValue { get; set; }

        }

        public class SelectedBranchAssingPatchWithBranchDto
        {
            public long PAB { get; set; }
            public int? PatchId { get; set; }
            public int? Progress { get; set; }
            public PatchStep? ProcessLevel { get; set; }
            public PatchStatus? Status { get; set; }
            public string? Message { get; set; }
            public string? ExtraValue { get; set; }
            public DateTime? StartTime { get; set; }
            public DateTime? Endtime { get; set; }
            public SelectBranchDto? branch { get; set; }


        }
    }
}
