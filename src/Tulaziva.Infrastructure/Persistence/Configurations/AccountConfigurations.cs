using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Infrastructure.Persistence.Configurations;

public class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("Favorites");

        builder.HasKey(f => new { f.UserId, f.AdId });

        // The favorites go with the ad when its owner deletes it. The users are never deleted.
        builder.HasOne(f => f.Ad).WithMany().HasForeignKey(f => f.AdId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => new { f.UserId, f.CreatedAtUtc });
        builder.HasIndex(f => f.AdId);
    }
}

public class AdReportConfiguration : IEntityTypeConfiguration<AdReport>
{
    public void Configure(EntityTypeBuilder<AdReport> builder)
    {
        builder.ToTable("AdReports");

        builder.HasKey(r => r.Id);

        // AdId is not a foreign key: the report outlives the ad (see AdReport).
        builder.Property(r => r.AdTitle).IsRequired().HasMaxLength(120);
        builder.Property(r => r.Details).HasMaxLength(1000);

        // Stored as text so the table can be read without knowing the enum values.
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(r => r.Reporter).WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Restrict);

        // A user reports an ad once.
        builder.HasIndex(r => new { r.AdId, r.ReporterId }).IsUnique();
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc });
    }
}

public class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("UserTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(t => t.Payload).HasMaxLength(256);

        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.Purpose });
    }
}

public class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("UserBlocks");

        builder.HasKey(b => new { b.BlockerId, b.BlockedId });

        // Both point to Users: restricting both avoids two cascade paths, and the users are never deleted (a deleted account is anonymized).
        builder.HasOne(b => b.Blocker).WithMany().HasForeignKey(b => b.BlockerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Blocked).WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.BlockedId);
    }
}

public class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("EmailLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.ToAddress).IsRequired().HasMaxLength(256);
        builder.Property(l => l.Subject).IsRequired().HasMaxLength(200);
        builder.Property(l => l.LastError).HasMaxLength(2000);

        // Stored as text so the table can be read without knowing the enum values.
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(l => l.CreatedAtUtc);
        builder.HasIndex(l => new { l.Status, l.CreatedAtUtc });
    }
}
