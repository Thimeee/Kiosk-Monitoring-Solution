using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Models
{
    [Table("JobStatus")]
    public class JobStatus
    {
        [Key]
        [Column("JSId")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int JSId { get; set; }

        [Column("JobStatus")]
        [StringLength(50)]
        public string? StatusName { get; set; }

        // 🔗 Navigation property for Jobs with this status
        public virtual ICollection<Job>? Jobs { get; set; }
    }
}
