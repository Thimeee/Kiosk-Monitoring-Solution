using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO
{
    public class BranchJobResponse<T>
    {
        public string? jobUser { get; set; }
        public string? jobId { get; set; }

        public T? jobRsValue { get; set; }
        public DateTime? jobEndTime { get; set; }




    }

    public class JobDownloadResponse
    {
        public string? jobMsg { get; set; }
        public int? jobStatus { get; set; }
        public double? jobProgress { get; set; }
        public long jobTotalBytes { get; set; }
        public long jobDownloadedBytes { get; set; }

    }



}


