namespace BranchMonitorFrontEnd.Models
{
    public class Branch
    {
        public int Id { get; set; }
        public string BranchId { get; set; }
        public string BranchName { get; set; }
        public string Location { get; set; }
        public int BranchActiveStatus { get; set; }
        public string KioskID { get; set; }
        public string KioskVersion { get; set; }
        public string KioskSeriNumber { get; set; }
        public string KioskName { get; set; }
        public string? KisokSessionKey { get; set; }
        public DateTime? BranchAddDatetime { get; set; }
        public string? BranchLicenseKey { get; set; }
        public int? BranchLicenseStatus { get; set; }
    }
}
