using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class CustomerDetailTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Detail_uses_customer_id_and_maps_profile_license_loyalty_and_inactive_accounts()
    {
        // Offset the users sequence so customer and user IDs differ.
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1)", $"staff-{Guid.NewGuid():N}@test.local");
        var (customerId, email) = await Seed.CustomerAsync(db.DataSource);
        var userId = Convert.ToInt32(await Seed.ScalarAsync(db.DataSource, "SELECT user_id FROM customers WHERE id=$1", customerId));
        Assert.NotEqual(customerId, userId);
        await Seed.ScalarAsync(db.DataSource,
            "UPDATE users SET fullname='Customer Detail',gender='other',phone='0901234567',avatar='https://example.test/avatar.png',is_active=FALSE,password_hash='never-expose' WHERE id=$1", userId);
        var issueDate = new DateOnly(2023, 4, 5);
        var expiryDate = new DateOnly(2030, 4, 5);
        await Seed.ScalarAsync(db.DataSource,
            "UPDATE customers SET license_number='LICENSE-001',license_type='B2',license_issue_date=$1,license_expiry_date=$2,license_img='https://example.test/license.png',loyalty_point=75 WHERE id=$3",
            issueDate, expiryDate, customerId);

        var repository = new NpgsqlCustomerRepository(db.DataSource);
        var result = await repository.FindByIdAsync(customerId, default);
        Assert.NotNull(result);
        Assert.Equal(customerId, result.Id);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(email, result.Email);
        Assert.Equal("Customer Detail", result.FullName);
        Assert.Equal("other", result.Gender);
        Assert.Equal("0901234567", result.Phone);
        Assert.Equal("https://example.test/avatar.png", result.AvatarUrl);
        Assert.False(result.IsActive);
        Assert.Equal("LICENSE-001", result.License.Number);
        Assert.Equal("B2", result.License.Type);
        Assert.Equal(issueDate, result.License.IssueDate);
        Assert.Equal(expiryDate, result.License.ExpiryDate);
        Assert.Equal("https://example.test/license.png", result.License.ImageUrl);
        Assert.Equal("approved", result.License.Status);
        Assert.Equal(75, result.Loyalty.Points);
        Assert.Null(await repository.FindByIdAsync(userId, default));
    }

    [Fact]
    public async Task Detail_preserves_nullable_fields_and_excludes_missing_logical_user()
    {
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource, "pending");
        await Seed.ScalarAsync(db.DataSource, "UPDATE users SET fullname=NULL WHERE id=(SELECT user_id FROM customers WHERE id=$1)", customerId);
        var repository = new NpgsqlCustomerRepository(db.DataSource);
        var result = await repository.FindByIdAsync(customerId, default);
        Assert.NotNull(result);
        Assert.Null(result.FullName);
        Assert.Null(result.Gender);
        Assert.Null(result.Phone);
        Assert.Null(result.AvatarUrl);
        Assert.Null(result.License.Number);
        Assert.Null(result.License.Type);
        Assert.Null(result.License.IssueDate);
        Assert.Null(result.License.ExpiryDate);
        Assert.Null(result.License.ImageUrl);
        Assert.Equal("pending", result.License.Status);
        Assert.Equal(0, result.Loyalty.Points);
        Assert.Null(await repository.FindByIdAsync(int.MaxValue, default));
        await Seed.ScalarAsync(db.DataSource, "DELETE FROM users WHERE id=$1", result.UserId);
        Assert.Null(await repository.FindByIdAsync(customerId, default));
    }
}
