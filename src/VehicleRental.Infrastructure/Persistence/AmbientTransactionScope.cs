using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// Phase 2 / D-5: một transaction dùng chung, chỉ nội bộ tầng hạ tầng.
///
/// Quyết toán là một transaction do <c>NpgsqlRentalLifecycleRepository.SettleCompletionAsync</c> sở hữu, nhưng các bước chuyển tiền nằm
/// sau các cổng (<see cref="Car_Rental_System_BE.Application.Features.Rentals.IRentalPaymentGate"/>, <see
/// cref="Car_Rental_System_BE.Application.Features.Rentals.IInvoiceGateway"/>, <see
/// cref="Car_Rental_System_BE.Application.Features.Rentals.ISubscriptionUsage"/>, <see
/// cref="Car_Rental_System_BE.Application.Features.Rentals.ILoyaltyAward"/>) mà implementation trước đây tự mở kết nối và tự commit.
/// Khoản hoàn tiền commit theo cách đó vẫn còn dù sau đó có lỗi - đúng lỗi mà kịch bản 0.3 của Phase 0 ghi nhận.
///
/// Khi scope đang mở, các adapter đó chạy trên kết nối và transaction của scope nên không còn commit
/// độc lập: hoặc tất cả cùng commit, hoặc tất cả cùng biến mất. Giá trị là một <see
/// cref="AsyncLocal{T}"/> nên nó đi theo chuỗi gọi của quyết toán và không nhìn thấy được từ request
/// khác chạy song song.
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

    /// <summary>Scope do transaction hiện tại trên chuỗi gọi này mở, hoặc <c>null</c>.</summary>
    public static AmbientTransactionScope? Current => CurrentScope.Value;

    public static AmbientTransactionScope Begin(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var scope = new AmbientTransactionScope(connection, transaction, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    public void Dispose() => CurrentScope.Value = _enclosing;
}
