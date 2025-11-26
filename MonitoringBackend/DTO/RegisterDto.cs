using System.ComponentModel.DataAnnotations;

namespace MonitoringBackend.DTO
{
    public class RegisterDto
    {

        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; }

    }
}
