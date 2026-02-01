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
    public virtual DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentCommand> AgentCommands => Set<AgentCommand>();
    public DbSet<OutboxEntry> OutboxEntries => Set<OutboxEntry>();
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<CommandTemplate> CommandTemplates => Set<CommandTemplate>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<TenantStatus> TenantStatuses => Set<TenantStatus>();
    public DbSet<TenantSubscriptionPlan> SubscriptionPlans => Set<TenantSubscriptionPlan>();
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
        
        b.Entity<Command>(e =>
        {
            e.HasKey(x => x.CommandId);
            e.Property(x => x.CommandName).HasMaxLength(200).IsRequired();
            e.Property(x => x.CommandType).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.DeviceId });
            e.HasIndex(x => new { x.TenantId, x.CommandName });
        });

        b.Entity<CommandTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            e.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
            e.Property(x => x.DeviceType).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.DeviceType, x.Category, x.DisplayOrder });
            e.HasIndex(x => x.IsActive);
        });

        // Use the instance property instead of capturing a local
        b.Entity<Device>()
            .HasQueryFilter(d => d.TenantId == _tenant.TenantId);
        
        b.Entity<Command>()
            .HasQueryFilter(c => c.TenantId == _tenant.TenantId);
        
        // Automatically pick up all IEntityTypeConfiguration<T> in this assembly
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        b.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.SubscriptionPlan).HasDefaultValue(SubscriptionPlan.Base);
            e.Property(x => x.PhoneNumber).HasMaxLength(500); // Encrypted, so longer than raw phone number
            e.Property(x => x.SmsNotificationsEnabled).HasDefaultValue(false);
        });
        
        b.Entity<UserPermission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Permission).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Permission }).IsUnique();
            e.HasIndex(x => x.UserId); // Non-unique index for efficient permission lookups by user
            e.HasOne(x => x.User)
                .WithMany(u => u.Permissions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TenantStatus>(e =>
        {
            e.HasKey(x => x.TenantStatusId);
            e.Property(x => x.TenantStatusText).HasMaxLength(25).IsRequired();
        });

        b.Entity<TenantSubscriptionPlan>(e =>
        {
            e.HasKey(x => x.SubscriptionPlanId);
            e.Property(x => x.SubscriptionPlanText).HasMaxLength(25).IsRequired();
        });

        b.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.TenantId);
            e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(250).IsRequired();
            e.HasOne(x => x.TenantStatus)
                .WithMany()
                .HasForeignKey(x => x.TenantStatusId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SubscriptionPlan)
                .WithMany()
                .HasForeignKey(x => x.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}