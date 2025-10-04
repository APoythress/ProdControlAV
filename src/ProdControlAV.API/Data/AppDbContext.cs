using System;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Models;
using ProdControlAV.Core.Models;

public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenant;
    public AppDbContext(DbContextOptions<AppDbContext> o, ITenantProvider t) : base(o) => _tenant = t;

    // Expose tenant through an instance member so EF parameterizes it per-context
    protected Guid CurrentTenantId => _tenant.TenantId;
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceAction> DeviceActions => Set<DeviceAction>();
    public DbSet<DeviceStatusLog> DeviceStatusLogs => Set<DeviceStatusLog>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentCommand> AgentCommands => Set<AgentCommand>();
    public DbSet<OutboxEntry> OutboxEntries => Set<OutboxEntry>();
// Ensure you already have: Devices, DeviceStatusHistory


    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Device>(e =>
        {
            e.HasKey(x => new { x.Id });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Ip).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Name });
        });
        
        b.Entity<DeviceAction>(e =>
        {
            e.HasKey(x => x.ActionId);
            e.Property(x => x.ActionName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ActionName });
        });
        
        b.Entity<OutboxEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.ProcessedUtc, x.CreatedUtc });
        });


        // Use the instance property instead of capturing a local
        b.Entity<Device>()
            .HasQueryFilter(d => d.TenantId == _tenant.TenantId);
        
        // Automatically pick up all IEntityTypeConfiguration<T> in this assembly
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        b.Entity<AppUser>()
            .HasKey(x => x.UserId);
    }
}