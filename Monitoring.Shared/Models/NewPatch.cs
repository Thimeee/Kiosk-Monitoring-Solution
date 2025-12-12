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
    public class NewPatch
    {
        [Key]
        public int PId { get; set; }

        [MaxLength(100)]
        public string? PatchVersion { get; set; }

        public DateTime? CreateDate { get; set; }

        // Foreign Key
        public int? PTId { get; set; }

        public string? Remark { get; set; }

        [MaxLength(100)]
        public string? PatchZipName { get; set; }

        [MaxLength(250)]
        public string? PatchZipPath { get; set; }

        public int? PatchActiveStatus { get; set; }

        [MaxLength(100)]
        public string? PatchFileName { get; set; }

        [MaxLength(250)]
        public string? PatchFilePath { get; set; }

        public int? PatchProcessLevel { get; set; }

        // Navigation Property
        [ForeignKey("PTId")]
        public PatchType? PatchTypes { get; set; }
    }
}
