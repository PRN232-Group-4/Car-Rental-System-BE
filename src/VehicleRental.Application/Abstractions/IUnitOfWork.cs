namespace Car_Rental_System_BE.Application.Abstractions;

/// <summary>
/// Transactional-work seam for use cases: lets Application code run a unit of work atomically
/// without ever seeing a connection, a transaction or raw SQL.
/// </summary>
/// <remarks>
/// Deliberately narrow: it exposes no <c>NpgsqlConnection</c>, <c>NpgsqlTransaction</c>,
/// <c>DbConnection</c>, <c>DbTransaction</c> or any other persistence type. Row locks
/// (<c>SELECT ... FOR UPDATE</c>) and advisory locks stay inside the repositories, so leaking this
/// abstraction can never leak SQL into a use case.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> inside a single transaction and returns its result.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);

    /// <summary>Runs <paramref name="work"/> inside a single transaction.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct);
}
