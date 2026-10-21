using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlAuditLogRepository(NpgsqlDataSource dataSource) : IAuditLogRepository
{
    private const string Source = """
        WITH audit AS (
            SELECT a.id,a.created_at,a.actor_user_id,actor.email AS actor_email,actor.fullname AS actor_name,
                CASE a.action WHEN 'role:add' THEN 'role_assigned' WHEN 'role:remove' THEN 'role_removed'
                    WHEN 'deactivate' THEN 'account_locked' WHEN 'activate' THEN 'account_unlocked'
                    WHEN 'sessions:revoke' THEN 'session_revoked' ELSE a.action END AS action,
                a.user_id,target.email AS target_email,target.fullname AS target_name,a.reason,a.ip_address,a.status
            FROM user_administration_audit a
            LEFT JOIN users actor ON actor.id=a.actor_user_id
            LEFT JOIN users target ON target.id=a.user_id
        )
        """;
    private const string Filter = """
         WHERE ($1::text IS NULL
             OR strpos(lower(COALESCE(actor_email,'')),lower($1))>0
             OR strpos(lower(COALESCE(actor_name,'')),lower($1))>0
             OR strpos(lower(COALESCE(target_email,'')),lower($1))>0
             OR strpos(lower(COALESCE(target_name,'')),lower($1))>0
             OR strpos(lower('AUD-'||id::text),lower($1))>0
             OR strpos(actor_user_id::text,$1)>0 OR strpos(user_id::text,$1)>0)
           AND ($2::text IS NULL OR action=$2)
        """;

    public async Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var count = new NpgsqlCommand(Source + "SELECT COUNT(*) FROM audit" + Filter, connection, transaction);
        AddFilters(count, query);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        await using var command = new NpgsqlCommand(Source + """
            SELECT id,created_at,actor_user_id,actor_email,actor_name,action,user_id,target_email,reason,ip_address,status
            FROM audit
            """ + Filter + " ORDER BY created_at DESC,id DESC LIMIT $3 OFFSET $4", connection, transaction);
        AddFilters(command, query);
        command.Parameters.AddWithValue(query.PageSize);
        command.Parameters.AddWithValue(((long)query.Page - 1) * query.PageSize);
        var items = new List<AuditLogItem>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                items.Add(new($"AUD-{reader.GetInt64(0)}", reader.GetDateTime(1), reader.GetInt32(2),
                    Text(reader, 3), Text(reader, 4), reader.GetString(5), reader.GetInt32(6), Text(reader, 7),
                    Text(reader, 8), Text(reader, 9), reader.GetString(10)));
        await transaction.CommitAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }

    private static void AddFilters(NpgsqlCommand command, AuditLogQuery query)
    {
        command.Parameters.AddWithValue((object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.Action ?? DBNull.Value);
    }

    private static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
