using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// Phase 2 / D-5: an ambient, infrastructure-internal transaction.
///
/// The settlement is one transaction owned by <c>NpgsqlRentalLifecycleRepository.SettleCompletionAsync</c>,
/// but the steps that move money live behind ports (<see cref="Car_Rental_System_BE.Application.Lifecycle.IRentalPaymentGate"/>,
/// <see cref="Car_Rental_System_BE.Application.Lifecycle.IInvoiceGateway"/>,
/// <see cref="Car_Rental_System_BE.Application.Lifecycle.ISubscriptionUsage"/>,
/// <see cref="Car_Rental_System_BE.Application.Lifecycle.ILoyaltyAward"/>) whose implementations used to open
/// their own connection and commit on their own. A refund committed that way survives a later failure - the
/// exact bug Phase 0 scenario 0.3 records.
///
/// While a scope is open, those adapters run on the scope's connection and transaction and therefore no
/// longer commit independently: they either all commit together or they all disappear. The value is an
/// <see cref="AsyncLocal{T}"/> so it flows down the settlement call chain and is invisible to any other
/// request running in parallel.
/// </summary>
internal sealed class AmbientTransactionScope : IDisposable
{
    private static readonly AsyncLocal<AmbientTransactionScope?> CurrentScope = new();

    private readonly AmbientTransactionScope? _enclosing;

    private AmbientTransactionScope(NpgsqlConnection connection, NpgsqlTransaction transaction, AmbientTransactionScope? enclosing)
    {
        Connection = connection;
        Transaction = transaction;
        _enclosing = enclosing;
    }

    public NpgsqlConnection Connection { get; }

    public NpgsqlTransaction Transaction { get; }

    /// <summary>The scope opened by the transaction currently on this call chain, or <c>null</c>.</summary>
    public static AmbientTransactionScope? Current => CurrentScope.Value;

    public static AmbientTransactionScope Begin(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var scope = new AmbientTransactionScope(connection, transaction, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    public void Dispose() => CurrentScope.Value = _enclosing;
}
