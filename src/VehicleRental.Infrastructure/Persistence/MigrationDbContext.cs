using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

// Separate from the application model: usable before any business tables exist.
internal sealed class MigrationDbContext : DbContext
{
    public MigrationDbContext(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
        : base(new DbContextOptionsBuilder<MigrationDbContext>().UseNpgsql(connection).Options)
    {
        if (transaction is not null) Database.UseTransaction(transaction);
    }

    public DbSet<MigrationHistoryRow> History => Set<MigrationHistoryRow>();
    public DbSet<PostgresRelationRow> Relations => Set<PostgresRelationRow>();
    public DbSet<PostgresNamespaceRow> Namespaces => Set<PostgresNamespaceRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var history = modelBuilder.Entity<MigrationHistoryRow>();
        history.ToTable("schema_migrations");
        history.HasKey(x => x.Id);
        history.Property(x => x.Id).HasColumnName("id").HasMaxLength(100);
        history.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();
        history.Property(x => x.AppliedAt).HasColumnName("applied_at").HasDefaultValueSql("now()");

        var relation = modelBuilder.Entity<PostgresRelationRow>();
        relation.HasNoKey().ToView("pg_class", "pg_catalog");
        relation.Property(x => x.Name).HasColumnName("relname").HasColumnType("name");
        relation.Property(x => x.NamespaceId).HasColumnName("relnamespace").HasColumnType("oid");

        var schema = modelBuilder.Entity<PostgresNamespaceRow>();
        schema.HasNoKey().ToView("pg_namespace", "pg_catalog");
        schema.Property(x => x.Id).HasColumnName("oid").HasColumnType("oid");
        schema.Property(x => x.Name).HasColumnName("nspname").HasColumnType("name");
    }
}

internal sealed class MigrationHistoryRow
{
    public string Id { get; set; } = null!;
    public string Checksum { get; set; } = null!;
    public DateTime AppliedAt { get; set; }
}

internal sealed class PostgresRelationRow
{
    public string Name { get; set; } = null!;
    public uint NamespaceId { get; set; }
}

internal sealed class PostgresNamespaceRow
{
    public uint Id { get; set; }
    public string Name { get; set; } = null!;
}
