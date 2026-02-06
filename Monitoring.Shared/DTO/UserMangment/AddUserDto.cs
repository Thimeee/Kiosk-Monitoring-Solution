using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO.UserMangment
{
    public class AddUserDto
    {
        public string? UId { get; set; }
        public string? UserId { get; set; }
        public string? Name { get; set; }
        public string? EmployeeNo { get; set; }
        public string? NIC { get; set; }
        public string? Mobile { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }

        public string[]? Roles { get; set; }
    }
}
