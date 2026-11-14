using Microsoft.EntityFrameworkCore;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Infrastructure.Persistence;

public class TulazivaDbContext : DbContext
{
    public TulazivaDbContext(DbContextOptions<TulazivaDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Ad> Ads => Set<Ad>();

    public DbSet<AdImage> AdImages => Set<AdImage>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();

    public DbSet<Favorite> Favorites => Set<Favorite>();

    public DbSet<AdReport> AdReports => Set<AdReport>();

    public DbSet<UserToken> UserTokens => Set<UserToken>();

    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();

    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TulazivaDbContext).Assembly);
    }
}
