using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BranchMonitorFrontEnd.Models
{
    public class BranchRemot
    {
        public int Rid { get; set; }

        public int Id { get; set; }

        public string BranchIP { get; set; } = string.Empty;

        public int RemotePort { get; set; }

        public int ServiceStatus { get; set; }
        public string RemotePassword { get; set; } = string.Empty;

        public virtual Branch Branch { get; set; }
    }
}
