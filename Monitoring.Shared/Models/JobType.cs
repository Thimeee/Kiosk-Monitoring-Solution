using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Models
{
    [Table("JobType")]
    public class JobType
    {
        [Key]
        [Column("JTId")]
        public int JTId { get; set; }

        [Column("JobType")]
        [StringLength(50)]
        public string? TypeName { get; set; }

        // Navigation
        //public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
    }
}
