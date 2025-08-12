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
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Device>(e =>
        {
            e.HasKey(x => new { x.Id, x.TenantId });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Ip).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Name });
        });

        b.Entity<DeviceAction>(e =>
        {
            e.HasKey(x => new { x.DeviceId, x.TenantId });
            e.Property(x => x.ActionName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ActionName });
        });

        // Use the instance property instead of capturing a local
        b.Entity<Device>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
    }
}