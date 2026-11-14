using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);

        // Stored as text ("User", "Admin") so the table can be read without knowing the enum values.
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(u => u.PreferredLanguage).IsRequired().HasMaxLength(5);
        builder.Property(u => u.TermsVersion).HasMaxLength(20);

        builder.Property(u => u.ProfilePhotoPath).HasMaxLength(300);
        builder.Property(u => u.ProfilePhotoContentType).HasMaxLength(50);

        // For the searches by distance that come with the ads.
        builder.HasIndex(u => new { u.RegistrationLatitude, u.RegistrationLongitude });
    }
}
