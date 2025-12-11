using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Monitoring.Shared.Models
{
    [Table("PatchTypes")]
    public class PatchType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PTId { get; set; }

        [Column("PatchTypeName", TypeName = "NVARCHAR(50)")]
        [MaxLength(50)]
        public string? PatchTypeName { get; set; }

        [Column("PatchTypeActiveStatus")]
        public int? PatchTypeActiveStatus { get; set; }

        // Navigation properties
        [JsonIgnore]
        public virtual ICollection<PatchScript>? PatchScripts { get; set; }
        [JsonIgnore]
        public virtual ICollection<NewPatch>? NewPatches { get; set; }
    }

}
