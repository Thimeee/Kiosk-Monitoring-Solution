using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Monitoring.Shared.Models
{
    [Table("Jobs")]
    public class Job
    {
        [Key]
        [Column("JId")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long JId { get; set; }  // BIGINT

        [Column("UserId")]
        [StringLength(450)]
        public string? UserId { get; set; }

        [Column("JobUId")]
        public string? JobUId { get; set; }


        [Column("JobMainStatus")]
        public int? JobMainStatus { get; set; }  // FK to JobStatus table


        [Column("JobDate")]
        public DateTime? JobDate { get; set; }

        [Column("JobStartTime")]
        public DateTime? JobStartTime { get; set; }

        [Column("JobEndTime")]
        public DateTime? JobEndTime { get; set; }

        [Column("JobMassage")]
        [StringLength(450)]
        public string? JobMassage { get; set; }

        [Column("JobName")]
        [StringLength(450)]
        public string? JobName { get; set; }

        [Column("JSId")]
        public int? JSId { get; set; }  // FK to JobStatus table

        [Column("JobActive")]
        public int? JobActive { get; set; }

        [Column("JTId")]
        public int? JTId { get; set; }  // FK to JobType table

        // 🔗 Navigation properties

        [ForeignKey("JSId")]
        public virtual JobStatus? JobStatus { get; set; }

        [ForeignKey("JTId")]
        public virtual JobType? JobType { get; set; }

        [JsonIgnore]
        public virtual ICollection<JobAssignBranch> JobAssignBranches { get; set; } = new List<JobAssignBranch>();

        //[ForeignKey("UserId")]
        //public virtual ApplicationUser? User { get; set; }
    }
}
