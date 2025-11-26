using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Monitoring.Shared.Models
{
    [Table("BranchRemot")]
    public class BranchRemot
    {
        [Key]
        public int Rid { get; set; }

        // Foreign Key to Branch table
        [Required]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string BranchIP { get; set; } = string.Empty;

        [Required]
        public int RemotePort { get; set; }

        [Required]
        public int ServiceStatus { get; set; }

        [Required]
        [StringLength(100)]
        public string RemotePassword { get; set; } = string.Empty;

        // Navigation Property
        [ForeignKey("Id")]
        public virtual Branch Branch { get; set; }
    }
}
