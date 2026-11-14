using Car_Rental_System_BE.Application.Common.Errors;
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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        int key;
        if (id is null)
        {
            var row = new FacilitiesRow { Name = name, Address = address, Phone = phone };
            await db.InsertAsync(row, ct);
            key = row.Id;
        }
        else
        {
            if (await db.Set<FacilitiesRow>().Where(f => f.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.Name, name)
                    .SetProperty(f => f.Address, address)
                    .SetProperty(f => f.Phone, f => phone ?? f.Phone), ct) == 0)
                throw new KeyNotFoundException("Facility was not found.");
            key = id.Value;
        }
        return await db.Set<FacilitiesRow>().Where(f => f.Id == key)
            .Select(f => new OrganizationUnit(f.Id, f.Name, f.Address, f.Phone)).SingleAsync(ct);
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

    private async Task<OrganizationUnit> SaveUnitAsync(string table,int? id,string name,string? address,CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (table == "facilities")
        {
            int key;
            if (id is null) { var row = new FacilitiesRow { Name = name, Address = address }; await db.InsertAsync(row, ct); key = row.Id; }
            else if (await db.Set<FacilitiesRow>().Where(f => f.Id == id).ExecuteUpdateAsync(s => s
                         .SetProperty(f => f.Name, name).SetProperty(f => f.Address, address), ct) == 0) throw new KeyNotFoundException();
            else key = id.Value;
            return await db.Set<FacilitiesRow>().Where(f => f.Id == key).Select(f => new OrganizationUnit(f.Id, f.Name, f.Address, null)).SingleAsync(ct);
        }
        if (id is null) { var row = new DepartmentsRow { Name = name }; await db.InsertAsync(row, ct); return new(row.Id, name, null); }
        if (await db.Set<DepartmentsRow>().Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, name), ct) == 0)
            throw new KeyNotFoundException();
        return new(id.Value, name, null);
    }
    private async Task DeleteAsync(string table,int id,CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        int deleted;
        try
        {
            if (table == "facilities")
            {
                // Xóa chi nhánh sẽ xóa những gì thuộc về nó (vùng địa lý và sự kiện của chúng xóa dây chuyền) và
                // gỡ liên kết nhân viên, xe và phiếu bảo trì, để đơn thuê, hóa đơn và thanh toán vẫn nguyên vẹn.
                await db.Set<GeofencesRow>().Where(g => g.FacilityId == id).ExecuteDeleteAsync(ct);
                await db.Set<StaffsRow>().Where(s => s.FacilityId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FacilityId, (int?)null), ct);
                await db.Set<VehiclesRow>().Where(v => v.FacilityId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FacilityId, (int?)null), ct);
                await db.Set<VehicleMaintenanceRow>().Where(m => m.FacilityId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FacilityId, (int?)null), ct);
                deleted = await db.Set<FacilitiesRow>().Where(f => f.Id == id).ExecuteDeleteAsync(ct);
            }
            else
            {
                deleted = await db.Set<DepartmentsRow>().Where(d => d.Id == id).ExecuteDeleteAsync(ct);
            }
        }
        catch (Exception ex) when (ex.SqlState() == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ConflictException(table == "facilities" ? "FACILITY_IN_USE" : "DEPARTMENT_IN_USE",
                "Bản ghi đang được tham chiếu bởi dữ liệu khác nên không thể xóa.");
        }
        if(deleted!=1)throw new KeyNotFoundException();
        await transaction.CommitAsync(ct);
    }
}
