using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        public virtual BranchRemot? Remote { get; set; }

        //// Navigation
        //public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
        //public virtual ICollection<BranchRemot> Remotes { get; set; } = new List<BranchRemot>();
    }
}
