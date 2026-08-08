using Microsoft.EntityFrameworkCore;

public class KnowledgeItem
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty; 
    public string TopicName { get; set; } = string.Empty; 
    public string ContentDetails { get; set; } = string.Empty; 
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class KnowledgeContext : DbContext
{
    public KnowledgeContext(DbContextOptions<KnowledgeContext> options) : base(options) { }
    public DbSet<KnowledgeItem> KnowledgeItems => Set<KnowledgeItem>();
}
