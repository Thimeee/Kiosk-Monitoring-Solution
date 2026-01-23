using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Monitoring.Shared.Enum;
using static Monitoring.Shared.DTO.PatchsDto.SelectedPatch;

namespace Monitoring.Shared.DTO.BranchDto
{
    public class SelectBranchDto
    {

        public int? Id { get; set; }

        public string? BranchId { get; set; }
        public string? BranchName { get; set; }

        public string? Location { get; set; }
        public TerminalActive? TerminalActiveStatus { get; set; } = 0;

        public string? TerminalId { get; set; }

        public string? TerminalSeriNumber { get; set; }
        public string? TerminalVersion { get; set; }

        public string? TerminalName { get; set; }

        public DateTime? TerminalAddDatetime { get; set; }

    }

    public class SelectBranchDtoWithSelectedPatchDto
    {

        public int? Id { get; set; }
        public string? BranchId { get; set; }
        public string? BranchName { get; set; }
        public string? Location { get; set; }
        public TerminalActive? TerminalActiveStatus { get; set; } = 0;
        public string? TerminalId { get; set; }
        public SelectedBranchAssingPatch? EnrollBranch { get; set; }
        public bool SelectPatchNotEnrollPatchStatus { get; set; }


    }

    public class PagedRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; }
        public string? Status { get; set; }
    }

    public class RequestNotPage
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public bool SelectPatch { get; set; }
        public int? PatchId { get; set; }

    }

    public class KioskData
    {
        public int? Number { get; set; }
        public string? BranchCode { get; set; }
        public string? TerminalId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Uptime { get; set; } = string.Empty;
        public string LastUpdate { get; set; } = string.Empty;
        public SelectBranchDto branchDto { get; set; } = new();
    }
}
