using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// Đoạn SQL duy nhất còn lại trong repository: các primitive khóa của PostgreSQL (advisory/dòng/bảng) và đồng hồ
/// database, những thứ LINQ không biểu diễn được. Mọi thứ khác (SELECT/INSERT/UPDATE/DELETE) đi qua EF Core. Mọi
/// lệnh gọi chạy trên kết nối/transaction hiện tại của context, nên thứ tự và vòng đời khóa không đổi.
/// </summary>
public enum RowLock { Update, Share, KeyShare, UpdateNoWait }

public static class DbLocks
{
    private static string Clause(RowLock mode) => mode switch
    {
        RowLock.Share => " FOR SHARE",
        RowLock.KeyShare => " FOR KEY SHARE",
        RowLock.UpdateNoWait => " FOR UPDATE NOWAIT",
        _ => " FOR UPDATE",
    };

    /// <summary>Mã lỗi PostgreSQL của exception từ ExecuteUpdate/Delete (thô) hoặc SaveChanges (đã bọc).</summary>
    public static string? SqlState(this Exception exception) => exception.Postgres()?.SqlState;

    public static PostgresException? Postgres(this Exception exception) =>
        exception as PostgresException ?? exception.InnerException as PostgresException;

    public static Task AdvisoryLockAsync(this DbContext db, long key, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);

    public static Task AdvisoryLockAsync(this DbContext db, int category, int key, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({category}, {key})", ct);

    /// <summary>
    /// Khóa điều phối dùng chung trong phạm vi transaction: một khóa duy nhất bảo vệ các bất biến về tình trạng
    /// trống giữa nhiều bảng (phân công, kiểm tra, sự cố, bảo trì, khôi phục, chỉnh sửa đơn). Mọi slice lấy đúng
    /// khóa này, theo đúng thứ tự cũ.
    /// </summary>
    public const long DispatcherLockKey = 724232001L;

    public static Task DispatcherLockAsync(this DbContext db, CancellationToken ct) =>
        db.AdvisoryLockAsync(DispatcherLockKey, ct);

    /// <summary>Khóa các bảng được chỉ định (định danh lấy từ EF model, không bao giờ từ dữ liệu nhập).</summary>
    public static Task LockTablesAsync(this DbContext db, string mode, CancellationToken ct, params Type[] rows)
    {
        var tables = string.Join(", ", rows.Select(r => Quote(TableOf(db, r))));
        var sql = "LOCK TABLE " + tables + " IN " + mode + " MODE";
        return db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    /// <summary><c>SELECT * FROM table WHERE id = ANY(ids) ORDER BY id FOR UPDATE</c> trên cột khóa của dòng.</summary>
    public static Task<List<T>> LockByKeysAsync<T>(this DbContext db, IEnumerable<int> keys, CancellationToken ct, RowLock mode = RowLock.Update) where T : class
    {
        var (table, key) = TableAndKey<T>(db);
        var ids = keys.Distinct().Order().ToArray();
        var sql = "SELECT * FROM " + Quote(table) + " WHERE " + Quote(key) + " = ANY({0}) ORDER BY " + Quote(key) + Clause(mode);
        return db.Set<T>().FromSqlRaw(sql, ids)
            .AsNoTracking().ToListAsync(ct);
    }

    public static async Task<T?> LockByKeyAsync<T>(this DbContext db, int key, CancellationToken ct, RowLock mode = RowLock.Update) where T : class =>
        (await db.LockByKeysAsync<T>([key], ct, mode)).SingleOrDefault();

    /// <summary>Khóa từng dòng có cột số nguyên <paramref name="column"/> nằm trong <paramref name="values"/>, theo thứ tự khóa.</summary>
    public static Task<List<T>> LockWhereAsync<T>(this DbContext db, string column, IEnumerable<int> values, CancellationToken ct, RowLock mode = RowLock.Update) where T : class
    {
        var (table, key) = TableAndKey<T>(db);
        var columnName = db.Model.FindEntityType(typeof(T))!.FindProperty(column)!.GetColumnName();
        var sql = "SELECT * FROM " + Quote(table) + " WHERE " + Quote(columnName) + " = ANY({0}) ORDER BY " + Quote(key) + Clause(mode);
        return db.Set<T>().FromSqlRaw(sql, values.Distinct().ToArray())
            .AsNoTracking().ToListAsync(ct);
    }

    /// <summary>Đồng hồ transaction của PostgreSQL dạng <c>timestamp</c>, giống hệt <c>now()</c> trong SQL trước đây.</summary>
    public static Task<DateTime> DatabaseNowAsync(this DbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateTime>("SELECT now()::timestamp AS \"Value\"").SingleAsync(ct);

    public static Task<DateTime> DatabaseUtcNowAsync(this DbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateTime>("SELECT (now() AT TIME ZONE 'utc') AS \"Value\"").SingleAsync(ct);

    public static Task<DateOnly> DatabaseTodayAsync(this DbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateOnly>("SELECT CURRENT_DATE AS \"Value\"").SingleAsync(ct);

    /// <summary>
    /// Chèn bên trong một savepoint và coi vi phạm unique là "đã tồn tại" (ON CONFLICT DO NOTHING), giữ
    /// transaction bao quanh vẫn dùng được. Trả về false khi dòng đã tồn tại.
    /// </summary>
    public static async Task<bool> TryInsertAsync<T>(this DbContext db, T row, CancellationToken ct) where T : class
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        var savepoint = "ef_insert_" + Guid.NewGuid().ToString("N");
        if (transaction is not null) await transaction.SaveAsync(savepoint, ct);
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.ReleaseAsync(savepoint, ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            if (transaction is not null) await transaction.RollbackAsync(savepoint, ct);
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    public static async Task<int> InsertAsync<T>(this DbContext db, T row, CancellationToken ct) where T : class
    {
        db.Add(row);
        var written = await db.SaveChangesAsync(ct);
        db.Entry(row).State = EntityState.Detached;
        return written;
    }

    private static string TableOf(DbContext db, Type row) => db.Model.FindEntityType(row)!.GetTableName()!;

    private static (string Table, string Key) TableAndKey<T>(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(T))!;
        return (entity.GetTableName()!, entity.FindPrimaryKey()!.Properties.Single().GetColumnName());
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
