using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;

public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenant;
    public AppDbContext(DbContextOptions<AppDbContext> o, ITenantProvider t) : base(o) => _tenant = t;

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceAction> DeviceActions => Set<DeviceAction>();

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
        
        var tenantId = _tenant.TenantId;
        b.Entity<Device>().HasQueryFilter(x => x.TenantId == tenantId);
    }
}