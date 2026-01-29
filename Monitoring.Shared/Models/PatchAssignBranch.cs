using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Monitoring.Shared.DTO;
using Monitoring.Shared.Enum;

namespace Monitoring.Shared.Models
{
    [Table("PatchAssignBranch")]
    public class PatchAssignBranch
    {
        [Key]
        [Column("PAB")]
        public long PAB { get; set; }

        [Column("Id")]
        public int? Id { get; set; }

        [Column("PId")]
        public int? PId { get; set; }

        [Column("ProcessLevel")]
        public PatchStep? ProcessLevel { get; set; }

        [Column("Status")]
        public PatchStatus? Status { get; set; }

        [Column("StartTime")]
        public DateTime? StartTime { get; set; }

        [Column("Endtime")]
        public DateTime? Endtime { get; set; }

        [Column("Message")]
        public string? Message { get; set; }

        [Column("BranchPatchLocation")]
        public string? BranchPatchLocation { get; set; }

        [Column("BranchBackupLocation")]
        public string? BranchBackupLocation { get; set; }

        [Column("ArrivingChunksBranch")]
        public string? ArrivingChunksBranch { get; set; }

        [Column("SendChunksBranch")]
        public string? SendChunksBranch { get; set; }

        [Column("JobUId")]
        public string? JobUId { get; set; }
        [Column("AttemptSteps")]
        public int? AttemptSteps { get; set; }

        // Navigation properties
        [ForeignKey("Id")]
        public Branch? Branch { get; set; }

        [ForeignKey("PId")]
        public NewPatch? NewPatch { get; set; }

        [Column("Progress")]
        public int? Progress { get; set; }
        [Column("IsFinalized")]
        public PatchIsFinalized? IsFinalized { get; set; }
    }
}
