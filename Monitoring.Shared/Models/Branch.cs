using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Monitoring.Shared.Models
{
    [Table("Branch")]
    public class Branch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string BranchId { get; set; }

        [Required]
        [StringLength(100)]
        public string BranchName { get; set; }

        [Required]
        [StringLength(100)]
        public string Location { get; set; }

        [Required]
        public int BranchActiveStatus { get; set; } = 0;

        [Required]
        [StringLength(100)]
        public string KioskID { get; set; }

        [Required]
        [StringLength(50)]
        public string KioskVersion { get; set; }

        [Required]
        [StringLength(150)]
        public string KioskSeriNumber { get; set; }

        [Required]
        [StringLength(50)]
        public string KioskName { get; set; }

        public string? KisokSessionKey { get; set; }
        public DateTime? BranchAddDatetime { get; set; }
        public string? BranchLicenseKey { get; set; }

        public int? BranchLicenseStatus { get; set; }


        ////// Navigation
        [JsonIgnore]
        public virtual ICollection<JobAssignBranch> JobAssignBranches { get; set; } = new List<JobAssignBranch>();
        [JsonIgnore]
        public virtual ICollection<BranchRemot> Remotes { get; set; } = new List<BranchRemot>();
    }
}
