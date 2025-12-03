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
        public DbSet<Job> Jobs { get; set; }
        public DbSet<JobType> JobTypes { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Job → User
    //        modelBuilder.Entity<Job>()
    //.HasOne<AppUser>()
    //.WithMany()
    //.HasForeignKey(j => j.UserId)
    //.IsRequired(false);

    //        // Job → Branch (Many Jobs per Branch)
    //        modelBuilder.Entity<Job>()
    //            .HasOne(j => j.Branch)
    //            .WithMany(b => b.Jobs)
    //            .HasForeignKey(j => j.BranchId)
    //            .OnDelete(DeleteBehavior.Restrict);

    //        // BranchRemot → Branch (one to one) 
    //        modelBuilder.Entity<Branch>()
    //      .HasOne(b => b.Remote)
    //      .WithOne(r => r.Branch)
    //    .HasForeignKey<BranchRemot>(r => r.Id)
    //      .OnDelete(DeleteBehavior.Cascade);



            // Job → JobType
            //modelBuilder.Entity<Job>()
            //    .HasOne(j => j.JobType)
            //    .WithMany(jt => jt.j)
            //    .HasForeignKey(j => j.JTId)
            //    .OnDelete(DeleteBehavior.Restrict);


        }



    }


}
