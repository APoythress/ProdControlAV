using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Models;

public sealed class UserTenantConfig : IEntityTypeConfiguration<UserTenant>
{
    public void Configure(EntityTypeBuilder<UserTenant> builder)
    {
        builder.ToTable("UserTenants");

        builder.HasKey(ut => new { ut.UserId, ut.TenantId });

        builder.Property(ut => ut.Role).IsRequired();

        builder.HasOne(ut => ut.User)
            .WithMany(u => u.Memberships) // requires AppUser.Memberships
            .HasForeignKey(ut => ut.UserId) // ONLY UserId is FK
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ut => ut.Tenant)
            .WithMany() // no back-collection required
            .HasForeignKey(ut => ut.TenantId)
            .OnDelete(DeleteBehavior.NoAction); // Prevent multiple cascade paths in SQL Server

        builder.HasIndex(ut => ut.UserId);
        builder.HasIndex(ut => ut.TenantId);

        // No AppUserId property mapped — EF will ignore the extra DB column.
    }
}