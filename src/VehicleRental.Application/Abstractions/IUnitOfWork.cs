namespace Car_Rental_System_BE.Application.Abstractions;

/// <summary>
/// Điểm nối giao dịch cho use case: cho phép code Application chạy một đơn vị công việc nguyên tử
/// mà không cần biết tới kết nối, transaction hay SQL thô.
/// </summary>
/// <remarks>
/// Cố ý giữ hẹp: không lộ <c>NpgsqlConnection</c>, <c>NpgsqlTransaction</c>, <c>DbConnection</c>,
/// <c>DbTransaction</c> hay bất kỳ kiểu lưu trữ nào. Khóa dòng (<c>SELECT ... FOR UPDATE</c>) và
/// advisory lock nằm trong repository, nên abstraction này không bao giờ làm rò SQL vào use case.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Chạy <paramref name="work"/> trong một transaction duy nhất và trả về kết quả.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);

    /// <summary>Chạy <paramref name="work"/> trong một transaction duy nhất.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct);
}
