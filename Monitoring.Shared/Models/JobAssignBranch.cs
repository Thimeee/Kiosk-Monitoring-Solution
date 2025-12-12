using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Models
{
    [Table("JobAssignBranch")]
    public class JobAssignBranch
    {
        [Key]
        [Column("JABId")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long JABId { get; set; }

        [Column("JId")]
        // Foreign key to Jobs table
        public long? JId { get; set; }

        // Foreign key to Branch table
        public int? Id { get; set; }

        public bool? IsPatch { get; set; }

        public int? ProcessLevel { get; set; }

        // Navigation Properties
        [ForeignKey("JId")]
        public Job? Jobs { get; set; }

        [ForeignKey("Id")]
        public Branch? Branch { get; set; }
    }
}
