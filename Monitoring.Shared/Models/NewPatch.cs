using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Models
{
    [Table("NewPatch")]
    public class NewPatch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PId { get; set; }

        [Column("patchVersion", TypeName = "NVARCHAR(100)")]
        [MaxLength(100)]
        public string? PatchVersion { get; set; }

        [Column("CreateDate", TypeName = "DATETIME")]
        public DateTime? CreateDate { get; set; }

        [Column("PTId")]
        public int? PTId { get; set; }

        [Column("Remark", TypeName = "NVARCHAR(MAX)")]
        public string? Remark { get; set; }

        [Column("PatchZipName", TypeName = "NVARCHAR(100)")]
        [MaxLength(100)]
        public string? PatchZipName { get; set; }

        [Column("PatchZipPath", TypeName = "NVARCHAR(150)")]
        [MaxLength(150)]
        public string? PatchZipPath { get; set; }

        [Column("PatchActiveStatus")]
        public int? PatchActiveStatus { get; set; }

        // Navigation property for foreign key
        [ForeignKey(nameof(PTId))]
        public virtual PatchType? PatchType { get; set; }
    }
}
