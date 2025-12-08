using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO
{
    public class BranchJobRequest<T>
    {
        public string? jobUser { get; set; }
        public string? jobId { get; set; }
        public DateTime? jobStartTime { get; set; }

        public T? jobRqValue { get; set; }

    }

    public class BranchJobRequestFast
    {
        public string? jobUser { get; set; }
        public DateTime? jobStartTime { get; set; }

    }


    public class FileDetails
    {
        public BranchFile? branch { get; set; }
        public ServerFile? server { get; set; }

    }

    public class BranchFile
    {
        public string? path { get; set; }
        public string? name { get; set; }
        public long? size { get; set; }
    }
    public class ServerFile
    {
        public string? path { get; set; }
        public string? name { get; set; }
        public long? size { get; set; }
    }
}
