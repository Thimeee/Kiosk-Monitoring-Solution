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
    [Table("NewPatch")]
    public class NewPatch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PId { get; set; }

        [MaxLength(100)]
        public string? PatchVersion { get; set; }

        public DateTime? CreateDate { get; set; }

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

        public int? ServerSendChunks { get; set; }

        public int? ServerArrivingChunks { get; set; }

        [ForeignKey(nameof(PTId))]
        public PatchType? PatchType { get; set; }
    }
}
