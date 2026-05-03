using Microsoft.EntityFrameworkCore;

namespace Experion.Api.Data;

public class ExperionDbContext : DbContext
{
    public ExperionDbContext(DbContextOptions<ExperionDbContext> options) : base(options) { }

    public DbSet<ConversationTurn> ConversationHistory => Set<ConversationTurn>();
    public DbSet<ActionMapping> ActionMappings => Set<ActionMapping>();
    public DbSet<SemanticCacheEntry> SemanticCache => Set<SemanticCacheEntry>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<ActivityEventRecord> ActivityEvents => Set<ActivityEventRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TenantConfig> Tenants => Set<TenantConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<UserProfile>().HasKey(u => u.UserId);
        b.Entity<TenantConfig>().HasKey(t => t.TenantId);
        b.Entity<ActionMapping>().HasIndex(a => new { a.TenantId, a.ActionKey });
        b.Entity<SemanticCacheEntry>().HasIndex(c => c.TenantId);
        b.Entity<ConversationTurn>().HasIndex(c => c.SessionId);
        b.Entity<ActivityEventRecord>().HasIndex(c => c.UserId);
    }
}
