using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Monitoring.Shared.Models;

namespace MonitoringBackend.Data
{
    public class AppDbContext : IdentityDbContext<AppUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Branch> Branches { get; set; }
        public DbSet<BranchRemot> Remote { get; set; }
        public DbSet<SFTPFolderPath> SFTPFolders { get; set; }


    }
}
