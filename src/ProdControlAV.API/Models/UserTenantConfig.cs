using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProdControlAV.API.Models;

public class UserTenantConfig : IEntityTypeConfiguration<UserTenant>
{
    public void Configure(EntityTypeBuilder<UserTenant> builder)
    {
        builder.ToTable("UserTenants");

        // Composite key to ensure uniqueness per user+tenant
        builder.HasKey(ut => new { ut.UserId, ut.TenantId });

        // Optional: constrain Role length
        builder.Property(ut => ut.Role)
            .HasMaxLength(50);

        // Relationships
        // builder.HasOne(ut => ut.Tenant)
        //     .WithMany(/* t => t.UserTenants */) // use the nav here if you have it
        //     .HasForeignKey(ut => ut.TenantId)
        //     .OnDelete(DeleteBehavior.Cascade);
    }
}