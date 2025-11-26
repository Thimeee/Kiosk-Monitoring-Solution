using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO
{
    public class APIResponseSingleValue
    {
        public string? Value { get; set; }
        public bool Status { get; set; }
        public int StatusCode { get; set; }
        public string? Ex { get; set; }
        public string? Message { get; set; }
    }

    public class APIResponseObjectValue<T>
    {
        public T? Value { get; set; }
        public bool Status { get; set; }
        public int StatusCode { get; set; }
        public string? Ex { get; set; }
        public string? Message { get; set; }
    }


    public class APIResponseCoustomizeList<T, H>
    {
        public H? Value { get; set; }
        public List<T>? ValueList { get; set; }
        public bool Status { get; set; }
        public int StatusCode { get; set; }
        public string? Ex { get; set; }
        public string? Message { get; set; }
    }
}
