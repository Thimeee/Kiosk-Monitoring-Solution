using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Models
{
    [Table("ServerFolderPath")]
    public class ServerFolderPath
    {
        [Key]
        [Column("SFId")]
        public int SFId { get; set; }

        [Column("ServerFolderPath")]
        [StringLength(200)]
        public string? ServerFolderPathValue { get; set; }

        [Column("Name")]
        [StringLength(50)]
        public string? Name { get; set; }
    }
}
