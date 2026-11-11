using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Floor> Floors => Set<Floor>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Ticket>().HasOne(t => t.Room).WithMany()
            .HasForeignKey(t => t.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Ticket>().HasOne(t => t.Equipment).WithMany()
            .HasForeignKey(t => t.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Ticket>().HasOne(t => t.Reporter).WithMany()
            .HasForeignKey(t => t.ReporterId).OnDelete(DeleteBehavior.Restrict);
    }
}
