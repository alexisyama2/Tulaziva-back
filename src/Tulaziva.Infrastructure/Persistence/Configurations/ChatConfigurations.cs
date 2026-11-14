using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");

        builder.HasKey(c => c.Id);

        // AdId is not a foreign key: the conversation outlives the ad (see Conversation.AdId).
        builder.Property(c => c.AdTitle).IsRequired().HasMaxLength(120);
        builder.Property(c => c.LastMessagePreview).HasMaxLength(200);

        // The users are never deleted together with their conversations.
        builder.HasOne(c => c.Initiator).WithMany().HasForeignKey(c => c.InitiatorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Recipient).WithMany().HasForeignKey(c => c.RecipientId).OnDelete(DeleteBehavior.Restrict);

        // One conversation per ad and user who writes about it.
        builder.HasIndex(c => new { c.AdId, c.InitiatorId }).IsUnique();
        builder.HasIndex(c => new { c.InitiatorId, c.LastMessageAtUtc });
        builder.HasIndex(c => new { c.RecipientId, c.LastMessageAtUtc });
    }
}

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body).HasMaxLength(2000);

        builder.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });
        builder.HasIndex(m => new { m.ConversationId, m.SenderId, m.ReadAtUtc });
    }
}

public class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("MessageAttachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.StoragePath).IsRequired().HasMaxLength(300);
        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);

        builder.HasOne(a => a.Message).WithMany(m => m.Attachments).HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.MessageId);
        builder.HasIndex(a => a.CreatedAtUtc);
    }
}
