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
        public DbSet<PatchScript> PatchScripts { get; set; }
        public DbSet<PatchType> PatchTypes { get; set; }
        public DbSet<NewPatch> NewPatches { get; set; }
        public DbSet<JobStatus> jobStatuses { get; set; }
        public DbSet<JobAssignBranch> jobAssignBranches { get; set; }


        //     protected override void OnModelCreating(ModelBuilder modelBuilder)
        //     {
        //         base.OnModelCreating(modelBuilder);

        //         //        // Job → User
        //         //        modelBuilder.Entity<Job>()
        //         //.HasOne<AppUser>()
        //         //.WithMany()
        //         //.HasForeignKey(j => j.UserId)
        //         //.IsRequired(false);

        //         //// Job → Branch (Many Jobs per Branch)
        //         //modelBuilder.Entity<Job>()
        //         //    .HasOne(j => j.)
        //         //    .WithMany(b => b.Jobs)
        //         //    .HasForeignKey(j => j.BranchId)
        //         //    .OnDelete(DeleteBehavior.Restrict);

        //         // BranchRemot → Branch (one to one) 
        //         modelBuilder.Entity<BranchRemot>()
        // .HasOne(r => r.Branch)
        // .WithMany(b => b.Remotes)
        // .HasForeignKey(r => r.Id);  // Id in BranchRemot points to Branch.Id





        //         //Job → JobType
        //         //modelBuilder.Entity<Job>()
        //         //    .HasOne(j => j.JobType)
        //         //    .WithMany(jt => jt.JTId)
        //         //    .HasForeignKey(j => j.JTId)
        //         //    .OnDelete(DeleteBehavior.Restrict);

        //         // PatchScript → PatchType (one to many) 
        //         modelBuilder.Entity<PatchScript>()
        // .HasOne(r => r.PatchType)
        // .WithMany(b => b.PatchScripts)
        // .HasForeignKey(r => r.PTId);

        //         modelBuilder.Entity<NewPatch>()
        //.HasOne(r => r.PatchTypes)
        //.WithMany(b => b.NewPatches)
        //.HasForeignKey(r => r.PTId);



        //         // JobAssignBranch → Job (Many to One)
        //         modelBuilder.Entity<JobAssignBranch>()
        //             .HasOne(jab => jab.Jobs)
        //             .WithMany(j => j.JobAssignBranches)
        //             .HasForeignKey(jab => jab.JId)
        //             .OnDelete(DeleteBehavior.Restrict);

        //         // JobAssignBranch → Branch (Many to One)
        //         modelBuilder.Entity<JobAssignBranch>()
        //             .HasOne(jab => jab.Branch)
        //             .WithMany(b => b.JobAssignBranches)
        //             .HasForeignKey(jab => jab.Id)
        //             .OnDelete(DeleteBehavior.Restrict);


        //     }



    }


}
