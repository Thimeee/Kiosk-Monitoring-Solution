using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Models
{
    [Table("PatchScripts")]
    public class PatchScript
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SId { get; set; }

        [Column("ScriptName", TypeName = "NVARCHAR(100)")]
        [MaxLength(100)]
        public string? ScriptName { get; set; }

        [Column("ScriptContenct", TypeName = "NVARCHAR(MAX)")]
        public string? ScriptContenct { get; set; }

        [Column("Version", TypeName = "NVARCHAR(150)")]
        [MaxLength(150)]
        public string? Version { get; set; }

        [Column("CreatedDate", TypeName = "DATETIME")]
        public DateTime? CreatedDate { get; set; }

        [Column("ScriptActiveStatus")]
        public int? ScriptActiveStatus { get; set; }

        [Column("PTId")]
        public int? PTId { get; set; }

        // Navigation property for foreign key
        [ForeignKey(nameof(PTId))]
        public virtual PatchType? PatchType { get; set; }
    }

}
