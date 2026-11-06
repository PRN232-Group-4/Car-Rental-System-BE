using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Organization;

public sealed class NpgsqlOrganizationRepository(NpgsqlDataSource dataSource) : IOrganizationRepository
{
    public Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct)=>UnitsAsync("departments",false,ct);
    public Task<OrganizationUnit> SaveDepartmentAsync(int? id,string name,CancellationToken ct)=>SaveUnitAsync("departments",id,name,null,ct);
    public Task DeleteDepartmentAsync(int id,CancellationToken ct)=>DeleteAsync("departments",id,ct);
    public async Task<OperationsPage<OrganizationUnit>> ListFacilitiesAsync(int page, int pageSize, CancellationToken ct)
    {
        Car_Rental_System_BE.Application.Features.Rentals.RentalQueryValidation.Pagination(page, pageSize);
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var query = db.Set<FacilitiesRow>().AsNoTracking();
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var items = offset > int.MaxValue ? new List<OrganizationUnit>() : await query.OrderByDescending(x => x.Id)
            .Skip((int)offset).Take(pageSize).Select(x => new OrganizationUnit(x.Id, x.Name, x.Address, x.Phone)).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(items, page, pageSize, total);
    }
    public async Task<OrganizationUnit> SaveFacilityAsync(int? id,string name,string? address,CancellationToken ct,string? phone=null)
    {
        var sql=id is null?"INSERT INTO facilities(name,address,phone) VALUES($1,$2,$3) RETURNING id,name,address,phone":"UPDATE facilities SET name=$1,address=$2,phone=COALESCE($3,phone) WHERE id=$4 RETURNING id,name,address,phone";
        await using var command=dataSource.CreateCommand(sql);
        Add(command,name,address,phone);
        if(id is not null) Add(command,id);
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)) throw new KeyNotFoundException("Facility was not found.");
        return new(reader.GetInt32(0),reader.GetString(1),S(reader,2),S(reader,3));
    }
    public Task DeleteFacilityAsync(int id,CancellationToken ct)=>DeleteAsync("facilities",id,ct);
    private async Task<IReadOnlyList<OrganizationUnit>> UnitsAsync(string table, bool address, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (table == "departments") return await db.Set<DepartmentsRow>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new OrganizationUnit(x.Id, x.Name, null, null)).ToListAsync(ct);
        return await db.Set<FacilitiesRow>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new OrganizationUnit(x.Id, x.Name, address ? x.Address : null, null)).ToListAsync(ct);
    }

    private async Task<OrganizationUnit> SaveUnitAsync(string table,int? id,string name,string? address,CancellationToken ct){var q=id is null?(table=="facilities"?"INSERT INTO facilities(name,address)VALUES($1,$2) RETURNING id,name,address":"INSERT INTO departments(name)VALUES($1) RETURNING id,name"):(table=="facilities"?"UPDATE facilities SET name=$1,address=$2 WHERE id=$3 RETURNING id,name,address":"UPDATE departments SET name=$1 WHERE id=$2 RETURNING id,name");await using var c=dataSource.CreateCommand(q);Add(c,name);if(table=="facilities")c.Parameters.AddWithValue((object?)address??DBNull.Value);if(id is not null)c.Parameters.AddWithValue(id.Value);await using var r=await c.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new KeyNotFoundException();return new(r.GetInt32(0),r.GetString(1),r.FieldCount>2?S(r,2):null);}
    private async Task DeleteAsync(string table,int id,CancellationToken ct){await using var c=dataSource.CreateCommand($"DELETE FROM {table} WHERE id=$1");c.Parameters.AddWithValue(id);if(await c.ExecuteNonQueryAsync(ct)!=1)throw new KeyNotFoundException();}
    private static string? S(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
}
