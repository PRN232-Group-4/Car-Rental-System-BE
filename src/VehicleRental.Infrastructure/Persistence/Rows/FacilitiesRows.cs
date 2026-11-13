using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("departments")]
public sealed class DepartmentsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
}

[Table("facilities")]
public sealed class FacilitiesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
    [Column("address", TypeName = "varchar(255)")]
    public string? Address { get; set; }
    [Column("phone", TypeName = "varchar(50)")]
    public string? Phone { get; set; }
}
