
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

        [Column("JobDate")]
        public DateTime? JobDate { get; set; }

        [Column("JobStartTime")]
        public DateTime? JobStartTime { get; set; }

        [Column("JobEndTime")]
        public DateTime? JobEndTime { get; set; }

        [Column("JobMassage")]
        [StringLength(450)]
        public string? JobMassage { get; set; }

        [Column("JobStatus")]
        public int? JobStatus { get; set; }

        [Column("JobActive")]
        public int? JobActive { get; set; }

        [Column("BranchId")]
        public int? BranchId { get; set; }

        [Column("JTId")]
        public int? JTId { get; set; }

        // 🔗 Navigation properties

        [ForeignKey("JTId")]
        public virtual JobType? JobType { get; set; }

        [ForeignKey("BranchId")]
        public virtual Branch? Branch { get; set; }


    }

}





