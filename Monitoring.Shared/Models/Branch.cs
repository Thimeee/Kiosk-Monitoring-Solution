using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Monitoring.Shared.Enum;

namespace Monitoring.Shared.Models
{
    [Table("Branch")]
    public class Branch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
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
        public TerminalActive TerminalActiveStatus { get; set; } = 0;

        [Required]
        [StringLength(100)]
        public string? TerminalId { get; set; }

        [Required]
        [StringLength(50)]
        public string? TerminalVersion { get; set; }

        [Required]
        [StringLength(150)]
        public string? TerminalSeriNumber { get; set; }

        [Required]
        [StringLength(50)]
        public string? TerminalName { get; set; }

        public string? TerminalSessionKey { get; set; }
        public DateTime? TerminalAddDatetime { get; set; }
        public string? TerminalLicenseKey { get; set; }

        public int? TerminalLicenseStatus { get; set; }

        public string? BranchFolderpath { get; set; }

        public string? ServerBranchFolderpath { get; set; }

        ////// Navigation
        [JsonIgnore]
        public virtual ICollection<JobAssignBranch> JobAssignBranches { get; set; } = new List<JobAssignBranch>();
        [JsonIgnore]
        public virtual ICollection<BranchRemot> Remotes { get; set; } = new List<BranchRemot>();
    }
}
