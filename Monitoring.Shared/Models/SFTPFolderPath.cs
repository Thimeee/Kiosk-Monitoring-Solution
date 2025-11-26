using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Monitoring.Shared.Models
{
    [Table("SFTPFolderPaths")]
    public class SFTPFolderPath
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // if SFTP_Id is auto-increment
        public int SFTP_Id { get; set; }

        [ForeignKey("Branch")]
        public int Id { get; set; } // Reference to Branch table

        [MaxLength(200)]
        public string? ServerMainPath { get; set; }

        [MaxLength(200)]
        public string? ServerBranchPath { get; set; }

        [MaxLength(200)]
        public string? BranchPath { get; set; }

        public int? ServerStatus { get; set; }

        // Navigation property
        public virtual Branch? Branch { get; set; }
    }
}
