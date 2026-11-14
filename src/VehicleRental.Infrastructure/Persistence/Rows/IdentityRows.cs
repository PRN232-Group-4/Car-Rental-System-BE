using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

// Các row lưu trữ phản chiếu schema hiện có do SQL quản lý. Không có navigation property hay quy ước khóa ngoại.
[Table("users")]
public sealed class UsersRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("email", TypeName = "varchar(255)")]
    public string Email { get; set; } = null!;
    [Column("fullname", TypeName = "varchar(255)")]
    public string? Fullname { get; set; }
    [Column("gender", TypeName = "varchar(255)")]
    public string? Gender { get; set; }
    [Column("password_hash", TypeName = "varchar(255)")]
    public string? PasswordHash { get; set; }
    [Column("avatar", TypeName = "varchar(255)")]
    public string? Avatar { get; set; }
    [Column("phone", TypeName = "varchar(255)")]
    public string? Phone { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; } = true;
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("authorization_version", TypeName = "integer")]
    public int AuthorizationVersion { get; set; }
    [Column("address", TypeName = "varchar(500)")]
    public string? Address { get; set; }
}

[Table("roles")]
public sealed class RolesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
}

[Table("user_roles")]
public sealed class UserRolesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("user_id", TypeName = "integer")]
    public int UserId { get; set; }
    [Column("role_id", TypeName = "integer")]
    public int RoleId { get; set; }
}

[Table("user_administration_audit")]
public sealed class UserAdministrationAuditRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("user_id", TypeName = "integer")]
    public int UserId { get; set; }
    [Column("actor_user_id", TypeName = "integer")]
    public int ActorUserId { get; set; }
    [Column("action", TypeName = "varchar(40)")]
    public string Action { get; set; } = null!;
    [Column("reason", TypeName = "varchar(1000)")]
    public string? Reason { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("ip_address", TypeName = "varchar(45)")]
    public string? IpAddress { get; set; }
    [Column("status", TypeName = "varchar(20)")]
    public string Status { get; set; } = "success";
}
