using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        // UNIQUE lower(email) is an expression index, created in the migration's SQL.
        e.HasIndex(u => u.ExternalId).IsUnique();
        e.HasOne<Client>().WithMany().HasForeignKey(u => u.ClientId);
        e.ToTable(t => t.HasCheckConstraint("ck_users_client_role", "(role = 'client') = (client_id IS NOT NULL)"));
    }
}
