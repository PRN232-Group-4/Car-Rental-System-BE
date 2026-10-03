using Car_Rental_System_BE.Application.Operations;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure;

public sealed class NpgsqlOperationsRepository(NpgsqlDataSource dataSource) : IOperationsRepository
{
    public async Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT c.license_number,c.license_type,c.license_issue_date,c.license_expiry_date,c.license_img,COALESCE(c.license_status,'pending') FROM customers c JOIN users u ON u.id=c.user_id WHERE u.email=$1");
        cmd.Parameters.AddWithValue(email); await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new( S(r,0), S(r,1), D(r,2), D(r,3), S(r,4), r.GetString(5)) : null;
    }
    public async Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense x, CancellationToken ct)
    {
        await using var cmd=dataSource.CreateCommand("UPDATE customers SET license_number=$1,license_type=$2,license_issue_date=$3,license_expiry_date=$4,license_img=$5,license_status='pending' WHERE user_id=(SELECT id FROM users WHERE email=$6) RETURNING license_number,license_type,license_issue_date,license_expiry_date,license_img,license_status");
        Add(cmd,x.Number,x.Type,x.IssueDate,x.ExpiryDate,x.ImageUrl,email); await using var r=await cmd.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct)) throw new KeyNotFoundException(); return new(S(r,0),S(r,1),D(r,2),D(r,3),S(r,4),r.GetString(5));
    }
    public async Task<CustomerLicense> ReviewLicenseAsync(int customerId,string status,CancellationToken ct)
    { await using var cmd=dataSource.CreateCommand("UPDATE customers SET license_status=$1 WHERE id=$2 RETURNING license_number,license_type,license_issue_date,license_expiry_date,license_img,license_status"); Add(cmd,status,customerId); await using var r=await cmd.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct))throw new KeyNotFoundException();return new(S(r,0),S(r,1),D(r,2),D(r,3),S(r,4),r.GetString(5)); }
    public Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct)=>UnitsAsync("departments",false,ct);
    public Task<OrganizationUnit> SaveDepartmentAsync(int? id,string name,CancellationToken ct)=>SaveUnitAsync("departments",id,name,null,ct);
    public Task DeleteDepartmentAsync(int id,CancellationToken ct)=>DeleteAsync("departments",id,ct);
    public Task<IReadOnlyList<OrganizationUnit>> ListFacilitiesAsync(CancellationToken ct)=>UnitsAsync("facilities",true,ct);
    public Task<OrganizationUnit> SaveFacilityAsync(int? id,string name,string? address,CancellationToken ct)=>SaveUnitAsync("facilities",id,name,address,ct);
    public Task DeleteFacilityAsync(int id,CancellationToken ct)=>DeleteAsync("facilities",id,ct);
    public async Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct){await using var c=dataSource.CreateCommand("SELECT id,name,fuel_type FROM vehicle_types ORDER BY name");await using var r=await c.ExecuteReaderAsync(ct);var a=new List<VehicleType>();while(await r.ReadAsync(ct))a.Add(new(r.GetInt32(0),r.GetString(1),S(r,2)));return a;}
    public async Task<VehicleType> SaveVehicleTypeAsync(int? id,string name,string? fuel,CancellationToken ct){var sql=id is null?"INSERT INTO vehicle_types(name,fuel_type) VALUES($1,$2) RETURNING id,name,fuel_type":"UPDATE vehicle_types SET name=$1,fuel_type=$2 WHERE id=$3 RETURNING id,name,fuel_type";await using var c=dataSource.CreateCommand(sql);Add(c,name,fuel);if(id is not null)c.Parameters.AddWithValue(id.Value);await using var r=await c.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new KeyNotFoundException();return new(r.GetInt32(0),r.GetString(1),S(r,2));}
    public Task DeleteVehicleTypeAsync(int id,CancellationToken ct)=>DeleteAsync("vehicle_types",id,ct);
    public async Task<VehiclePage> SearchVehiclesAsync(int? type,int? facility,string? status,DateTime? from,DateTime? to,int page,int size,CancellationToken ct)
    { const string filter=" WHERE ($1::int IS NULL OR v.vehicle_type_id=$1::int) AND ($2::int IS NULL OR v.facility_id=$2::int) AND ($3::text IS NULL OR v.status=$3::text) AND ($4::timestamp IS NULL OR NOT EXISTS (SELECT 1 FROM rentals r LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE r.vehicle_id=v.id AND r.status NOT IN ('cancelled','completed') AND COALESCE(h.start_time,l.start_time)<$5::timestamp AND COALESCE(h.end_time,l.end_time)>$4::timestamp)) ";
      await using var count=dataSource.CreateCommand("SELECT COUNT(*) FROM vehicles v"+filter);Add(count,type,facility,status,from,to);var total=Convert.ToInt32(await count.ExecuteScalarAsync(ct));
      await using var c=dataSource.CreateCommand("SELECT v.id,v.vehicle_type_id,t.name,v.facility_id,f.name,COALESCE(v.status,'available'),v.brand,v.model,v.color,v.year_of_manufacture,v.rental_price_hourly,v.rental_price_longterm,v.description,v.main_img_url,v.current_location FROM vehicles v JOIN vehicle_types t ON t.id=v.vehicle_type_id LEFT JOIN facilities f ON f.id=v.facility_id"+filter+" ORDER BY v.id LIMIT $6 OFFSET $7");Add(c,type,facility,status,from,to,size,(page-1)*size);await using var r=await c.ExecuteReaderAsync(ct);var a=new List<Vehicle>();while(await r.ReadAsync(ct))a.Add(ReadVehicle(r));return new(a,page,size,total); }
    public async Task<Vehicle?> GetVehicleAsync(int id,CancellationToken ct){
        await using var c=dataSource.CreateCommand("SELECT v.id,v.vehicle_type_id,t.name,v.facility_id,f.name,COALESCE(v.status,'available'),v.brand,v.model,v.color,v.year_of_manufacture,v.rental_price_hourly,v.rental_price_longterm,v.description,v.main_img_url,v.current_location FROM vehicles v JOIN vehicle_types t ON t.id=v.vehicle_type_id LEFT JOIN facilities f ON f.id=v.facility_id WHERE v.id=$1");
        c.Parameters.AddWithValue(id);
        await using var r=await c.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct)) return null;
        var vehicle = ReadVehicle(r);
        await r.CloseAsync();
        return vehicle with { Detail = await LoadVehicleDetailAsync(id, ct) };
    }
    public async Task<Vehicle> SaveVehicleAsync(int? id,Vehicle v,CancellationToken ct){
        var sql=id is null?"INSERT INTO vehicles(vehicle_type_id,facility_id,status,brand,model,color,year_of_manufacture,rental_price_hourly,rental_price_longterm,description,main_img_url,current_location) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12) RETURNING id":"UPDATE vehicles SET vehicle_type_id=$1,facility_id=$2,status=$3,brand=$4,model=$5,color=$6,year_of_manufacture=$7,rental_price_hourly=$8,rental_price_longterm=$9,description=$10,main_img_url=$11,current_location=$12,modified_at=now() WHERE id=$13 RETURNING id";
        await using var c=dataSource.CreateCommand(sql);
        Add(c,v.VehicleTypeId,v.FacilityId,v.Status,v.Brand,v.Model,v.Color,v.YearOfManufacture,v.HourlyPrice,v.LongtermPrice,v.Description,v.MainImageUrl,v.Location);
        if(id is not null)c.Parameters.AddWithValue(id.Value);
        var result=await c.ExecuteScalarAsync(ct);
        if(result is not int vehicleId)throw new KeyNotFoundException();
        if(v.Detail is not null) await SaveVehicleDetailAsync(vehicleId, v.Detail, ct);
        return (await GetVehicleAsync(vehicleId,ct))!;
    }
    public async Task<RentalPolicy> CreatePolicyAsync(RentalPolicy x,CancellationToken ct){await using var c=dataSource.CreateCommand("INSERT INTO rental_policies(vehicle_type_id,name,vat_rate,overtime_fee_rate,deposit_percentage,is_active)VALUES($1,$2,$3,$4,$5,$6) RETURNING id,vehicle_type_id,name,vat_rate,overtime_fee_rate,deposit_percentage,is_active");Add(c,x.VehicleTypeId,x.Name,x.VatRate,x.OvertimeFeeRate,x.DepositPercentage,x.IsActive);await using var r=await c.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new InvalidOperationException("Rental policy could not be created.");return ReadPolicy(r);}
    public async Task<IReadOnlyList<RentalPolicy>> ListPoliciesAsync(CancellationToken ct){await using var c=dataSource.CreateCommand("SELECT id,vehicle_type_id,name,vat_rate,overtime_fee_rate,deposit_percentage,is_active FROM rental_policies ORDER BY id");await using var r=await c.ExecuteReaderAsync(ct);var a=new List<RentalPolicy>();while(await r.ReadAsync(ct))a.Add(ReadPolicy(r));return a;}
    public async Task<Rental> CreateRentalAsync(string email,int vehicleId,int? policyId,string type,DateTime start,DateTime end,bool subscription,int? subscriptionId,bool hasDriver,CancellationToken ct){if(end<=start)throw new ArgumentException("End time must be after start time.");await using var cn=await dataSource.OpenConnectionAsync(ct);await using var tx=await cn.BeginTransactionAsync(ct);var customer=await ScalarAsync(cn,tx,"SELECT c.id FROM customers c JOIN users u ON u.id=c.user_id WHERE u.email=$1",email,ct);if(customer is not int cid)throw new KeyNotFoundException();if(subscription){var needed=(decimal)(end-start).TotalHours;await using var sub=new NpgsqlCommand("SELECT remaining_hours FROM customer_subscriptions WHERE id=$1 AND customer_id=$2 AND status='active' AND start_date<=CURRENT_DATE AND end_date>=CURRENT_DATE FOR UPDATE",cn,tx);Add(sub,subscriptionId,cid);await using var sr=await sub.ExecuteReaderAsync(ct);if(!await sr.ReadAsync(ct))throw new KeyNotFoundException("Active subscription was not found.");var remaining=sr.IsDBNull(0)?(decimal?)null:sr.GetDecimal(0);await sr.CloseAsync();if(remaining is not null&&remaining<needed)throw new InvalidOperationException("Subscription does not have enough remaining hours.");if(remaining is not null){await using var deduct=new NpgsqlCommand("UPDATE customer_subscriptions SET remaining_hours=remaining_hours-$1 WHERE id=$2",cn,tx);Add(deduct,needed,subscriptionId);await deduct.ExecuteNonQueryAsync(ct);}}if(!subscription){await using var license=new NpgsqlCommand("SELECT license_status FROM customers WHERE id=$1",cn,tx);license.Parameters.AddWithValue(cid);if(!string.Equals((await license.ExecuteScalarAsync(ct))?.ToString(),"approved",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("An approved driver license is required.");}var conflict=await ScalarAsync(cn,tx,"SELECT EXISTS(SELECT 1 FROM rentals r LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE r.vehicle_id=$1 AND r.status NOT IN ('cancelled','completed') AND COALESCE(h.start_time,l.start_time)<$3 AND COALESCE(h.end_time,l.end_time)>$2)",new object?[]{vehicleId,start,end},ct);if(conflict is true)throw new InvalidOperationException("Vehicle is unavailable.");var price=await ScalarAsync(cn,tx,type=="hourly"?"SELECT rental_price_hourly FROM vehicles WHERE id=$1":"SELECT rental_price_longterm FROM vehicles WHERE id=$1",vehicleId,ct);if(price is not decimal unit)throw new KeyNotFoundException();var hours=(decimal)(end-start).TotalHours;var total=type=="hourly"?unit*hours:unit*Math.Max(1,Math.Ceiling(hours/(24*30)));decimal vat=0,deposit=0;if(policyId is not null){await using var po=new NpgsqlCommand("SELECT vat_rate,deposit_percentage FROM rental_policies WHERE id=$1 AND is_active=true",cn,tx);po.Parameters.AddWithValue(policyId.Value);await using var rr=await po.ExecuteReaderAsync(ct);if(!await rr.ReadAsync(ct))throw new InvalidOperationException("Policy is not active.");vat=total*rr.GetDecimal(0)/100;deposit=total*rr.GetDecimal(1)/100;}var rentalId=await ScalarAsync(cn,tx,"INSERT INTO rentals(customer_id,vehicle_id,policy_id,customer_subscription_id,is_subscription,rental_type,status,vat_price,deposit_price,total_price)VALUES($1,$2,$3,$4,$5,$6,'pending',$7,$8,$9) RETURNING id",new object?[]{cid,vehicleId,policyId,subscriptionId,subscription,type,vat,deposit,total+vat},ct);if(rentalId is not int rid)throw new InvalidOperationException();if(type=="hourly"){await using var d=new NpgsqlCommand("INSERT INTO hour_rental_detail(rental_id,start_time,end_time,has_driver,hourly_price_snapshot)VALUES($1,$2,$3,$4,$5)",cn,tx);Add(d,rid,start,end,hasDriver,unit);await d.ExecuteNonQueryAsync(ct);}else{await using var d=new NpgsqlCommand("INSERT INTO longterm_rental_contract(rental_id,start_time,end_time,monthly_price_snapshot)VALUES($1,$2,$3,$4)",cn,tx);Add(d,rid,start,end,unit);await d.ExecuteNonQueryAsync(ct);}if(!subscription){await using var inv=new NpgsqlCommand("INSERT INTO invoices(rental_id,invoice_number,amount,vat_amount)VALUES($1,$2,$3,$4)",cn,tx);Add(inv,rid,$"INV-{DateTime.UtcNow:yyyyMMdd}-{rid}",total+vat+deposit,vat);await inv.ExecuteNonQueryAsync(ct);}await tx.CommitAsync(ct);return (await GetRentalAsync(rid,ct))!;}
    public async Task<Rental?> GetRentalAsync(int id,CancellationToken ct){await using var c=dataSource.CreateCommand("SELECT r.id,r.customer_id,r.vehicle_id,r.rental_type,r.status,r.is_subscription,r.vat_price,r.deposit_price,r.overtime_price,r.total_price,COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time) FROM rentals r LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE r.id=$1");c.Parameters.AddWithValue(id);await using var r=await c.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?ReadRental(r):null;}
    public async Task<Rental> ChangeRentalStatusAsync(int id,string status,CancellationToken ct){var old=await GetRentalAsync(id,ct)??throw new KeyNotFoundException();var allowed=(old.Status,status) switch {("pending","cancelled")or("pending","pre_inspection")or("pre_inspection","renting")or("renting","post_inspection")or("post_inspection","completed")=>true,_=>false};if(!allowed)throw new InvalidOperationException("Invalid rental state transition.");await RequireInspectionForStatusAsync(id,status,ct);await using var c=dataSource.CreateCommand("UPDATE rentals SET status=$1 WHERE id=$2");Add(c,status,id);await c.ExecuteNonQueryAsync(ct);return (await GetRentalAsync(id,ct))!;}
    public async Task AssignAsync(int rental,int? driver,string assignedByEmail,CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using (var lockRental=new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)",cn,tx)) { lockRental.Parameters.AddWithValue(rental); await lockRental.ExecuteNonQueryAsync(ct); }
        if(driver is not null) await using (var lockDriver=new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)",cn,tx)) { lockDriver.Parameters.AddWithValue(driver.Value); await lockDriver.ExecuteNonQueryAsync(ct); }

        int vehicle; DateTime start; DateTime end; string rentalStatus;
        await using (var rentalCommand=new NpgsqlCommand("SELECT r.vehicle_id,COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time),r.status FROM rentals r LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE r.id=$1 FOR UPDATE OF r",cn,tx))
        {
            rentalCommand.Parameters.AddWithValue(rental);
            await using var reader=await rentalCommand.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct)) throw new KeyNotFoundException();
            vehicle=reader.GetInt32(0);
            if(reader.IsDBNull(1)||reader.IsDBNull(2)) throw new InvalidOperationException("Rental schedule is incomplete.");
            start=reader.GetDateTime(1); end=reader.GetDateTime(2); rentalStatus=reader.GetString(3);
        }
        if(rentalStatus!="pre_inspection") throw new InvalidOperationException("Driver assignment requires a successful pre-rental inspection.");
        await using (var inspection=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND inspection_type='pre_rental' AND is_ok=true)",cn,tx))
        {
            inspection.Parameters.AddWithValue(rental);
            if(!(bool)(await inspection.ExecuteScalarAsync(ct))!) throw new InvalidOperationException("Driver assignment requires a successful pre-rental inspection.");
        }
        if(driver is not null)
        {
            await using var driverCheck=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM staffs s JOIN users u ON u.id=s.user_id WHERE s.id=$1 AND s.is_driver=true AND u.is_active=true AND s.license_expiry_date>=CURRENT_DATE)",cn,tx);
            driverCheck.Parameters.AddWithValue(driver.Value);
            if(!(bool)(await driverCheck.ExecuteScalarAsync(ct))!) throw new InvalidOperationException("Driver must be active, qualified, and have a valid license.");
            await using var conflict=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM vehicle_assignments a JOIN rentals r ON r.id=a.rental_id LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE a.driver_staff_id=$1 AND a.rental_id<>$4 AND a.status='assigned' AND r.status NOT IN ('cancelled','completed') AND COALESCE(h.start_time,l.start_time)<$3 AND COALESCE(h.end_time,l.end_time)>$2)",cn,tx);
            Add(conflict,driver.Value,start,end,rental);
            if((bool)(await conflict.ExecuteScalarAsync(ct))!) throw new InvalidOperationException("Driver is unavailable for the rental period.");
        }
        await using (var closeCurrent=new NpgsqlCommand("UPDATE vehicle_assignments SET status='reassigned' WHERE rental_id=$1 AND status='assigned'",cn,tx)) { closeCurrent.Parameters.AddWithValue(rental); await closeCurrent.ExecuteNonQueryAsync(ct); }
        await using (var insert=new NpgsqlCommand("INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status,assigned_by)VALUES($1,$2,$3,'assigned',$4)",cn,tx)) { Add(insert,rental,vehicle,driver,assignedByEmail); await insert.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driver,DateTime from,DateTime to,CancellationToken ct)
    {
        await using var c=dataSource.CreateCommand("SELECT a.id,a.rental_id,a.vehicle_id,a.driver_staff_id,COALESCE(u.fullname,u.email),COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time),r.status FROM vehicle_assignments a JOIN rentals r ON r.id=a.rental_id JOIN staffs s ON s.id=a.driver_staff_id JOIN users u ON u.id=s.user_id LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE a.status='assigned' AND r.status NOT IN ('cancelled','completed') AND ($1 IS NULL OR a.driver_staff_id=$1) AND COALESCE(h.start_time,l.start_time)<$3 AND COALESCE(h.end_time,l.end_time)>$2 ORDER BY COALESCE(h.start_time,l.start_time),a.id");
        Add(c,driver,from,to); await using var r=await c.ExecuteReaderAsync(ct); var schedules=new List<DriverScheduleEntry>();
        while(await r.ReadAsync(ct)) schedules.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),r.GetDateTime(5),r.GetDateTime(6),r.GetString(7)));
        return schedules;
    }
    public async Task<Inspection> AddInspectionAsync(int rental,int staff,string type,bool ok,decimal? odo,decimal? fuel,string? notes,string? images,CancellationToken ct){var r=await GetRentalAsync(rental,ct)??throw new KeyNotFoundException();var required=type=="pre_rental"?"pending":"renting";if(r.Status!=required)throw new InvalidOperationException("Inspection is not allowed in current rental state.");await using var c=dataSource.CreateCommand("INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,odometer_km,fuel_battery_level,damage_notes,img_urls,inspected_at)VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,now()) RETURNING id,inspection_type,is_ok,odometer_km,fuel_battery_level,damage_notes,img_urls,inspected_at");Add(c,rental,r.VehicleId,staff,type,ok,odo,fuel,notes,images);await using var x=await c.ExecuteReaderAsync(ct);await x.ReadAsync(ct);var output=new Inspection(x.GetInt32(0),rental,x.GetString(1),x.GetBoolean(2),x.IsDBNull(3)?null:x.GetDecimal(3),x.IsDBNull(4)?null:x.GetDecimal(4),S(x,5),S(x,6),x.GetDateTime(7));await ChangeRentalStatusAsync(rental,type=="pre_rental"?(ok?"pre_inspection":"cancelled"):("post_inspection"),ct);if(type=="post_rental"&&!ok)await SaveMaintenanceAsync(null,new(0,r.VehicleId,null,null,"open","inspection",DateOnly.FromDateTime(DateTime.UtcNow),false,notes,null),ct);if(type=="post_rental"&&ok){await using var refund=dataSource.CreateCommand("INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method) SELECT r.id,i.id,r.customer_id,false,r.deposit_price,'requested','refund','deposit' FROM rentals r JOIN invoices i ON i.rental_id=r.id WHERE r.id=$1 AND r.is_subscription=false AND r.deposit_price>0 AND i.status='paid' AND NOT EXISTS(SELECT 1 FROM payments p WHERE p.rental_id=r.id AND p.type='refund' AND p.method='deposit')");refund.Parameters.AddWithValue(rental);await refund.ExecuteNonQueryAsync(ct);}return output;}
    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool active,CancellationToken ct){await using var c=dataSource.CreateCommand("SELECT id,name,description,price,duration_days,included_hours,is_active FROM subscription_plans WHERE ($1=false OR is_active=true) ORDER BY id");c.Parameters.AddWithValue(active);await using var r=await c.ExecuteReaderAsync(ct);var a=new List<SubscriptionPlan>();while(await r.ReadAsync(ct))a.Add(ReadPlan(r));return a;}
    public async Task<SubscriptionPlan?> GetPlanAsync(int id,CancellationToken ct){await using var c=dataSource.CreateCommand("SELECT id,name,description,price,duration_days,included_hours,is_active FROM subscription_plans WHERE id=$1");c.Parameters.AddWithValue(id);await using var r=await c.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?ReadPlan(r):null;}
    public async Task DeletePlanAsync(int id,CancellationToken ct){await using var cn=await dataSource.OpenConnectionAsync(ct);await using var tx=await cn.BeginTransactionAsync(ct);var used=await ScalarAsync(cn,tx,"SELECT EXISTS(SELECT 1 FROM customer_subscriptions WHERE plan_id=$1)",id,ct);await using var c=new NpgsqlCommand(used is true?"UPDATE subscription_plans SET is_active=false WHERE id=$1":"DELETE FROM subscription_plans WHERE id=$1",cn,tx);c.Parameters.AddWithValue(id);if(await c.ExecuteNonQueryAsync(ct)!=1)throw new KeyNotFoundException();await tx.CommitAsync(ct);}
    public async Task<SubscriptionPlan> SavePlanAsync(int? id,SubscriptionPlan x,CancellationToken ct){var q=id is null?"INSERT INTO subscription_plans(name,description,price,duration_days,included_hours,is_active)VALUES($1,$2,$3,$4,$5,$6) RETURNING id":"UPDATE subscription_plans SET name=$1,description=$2,price=$3,duration_days=$4,included_hours=$5,is_active=$6 WHERE id=$7 RETURNING id";await using var c=dataSource.CreateCommand(q);Add(c,x.Name,x.Description,x.Price,x.DurationDays,x.IncludedHours,x.IsActive);if(id is not null)c.Parameters.AddWithValue(id.Value);var key=await c.ExecuteScalarAsync(ct);if(key is not int value)throw new KeyNotFoundException();return (await ListPlansAsync(false,ct)).Single(x=>x.Id==value);}
    public async Task<int> BuyPlanAsync(string email,int plan,CancellationToken ct){await using var c=dataSource.CreateCommand("INSERT INTO customer_subscriptions(customer_id,plan_id,start_date,end_date,remaining_hours) SELECT c.id,p.id,CURRENT_DATE,CURRENT_DATE+p.duration_days,p.included_hours FROM customers c CROSS JOIN subscription_plans p JOIN users u ON u.id=c.user_id WHERE u.email=$1 AND p.id=$2 AND p.is_active=true RETURNING id");Add(c,email,plan);return (int?)await c.ExecuteScalarAsync(ct)??throw new KeyNotFoundException();}
    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email,CancellationToken ct)
    {
        await using var c=dataSource.CreateCommand("SELECT cs.id,cs.customer_id,cs.plan_id,p.name,cs.start_date,cs.end_date,p.included_hours,cs.remaining_hours,cs.status FROM customer_subscriptions cs JOIN customers cu ON cu.id=cs.customer_id JOIN users u ON u.id=cu.user_id JOIN subscription_plans p ON p.id=cs.plan_id WHERE u.email=$1 ORDER BY cs.id DESC");
        Add(c,email);
        await using var r=await c.ExecuteReaderAsync(ct);
        var a=new List<CustomerSubscription>();
        while(await r.ReadAsync(ct))a.Add(ReadCustomerSubscription(r));
        return a;
    }
    public async Task<int> ExpireSubscriptionsAsync(DateOnly today,CancellationToken ct){await using var c=dataSource.CreateCommand("UPDATE customer_subscriptions SET status='expired' WHERE status='active' AND end_date<$1");c.Parameters.AddWithValue(today);return await c.ExecuteNonQueryAsync(ct);}
    public async Task<IReadOnlyList<ExpiringSubscription>> ListSubscriptionsToRemindAsync(DateOnly today,int withinDays,CancellationToken ct){await using var c=dataSource.CreateCommand("SELECT cs.id,u.email,COALESCE(u.fullname,u.email),cs.end_date,cs.remaining_hours FROM customer_subscriptions cs JOIN customers cu ON cu.id=cs.customer_id JOIN users u ON u.id=cu.user_id WHERE cs.status='active' AND cs.reminder_sent_at IS NULL AND cs.end_date>=$1 AND cs.end_date<=$1+$2 ORDER BY cs.end_date");Add(c,today,withinDays);await using var r=await c.ExecuteReaderAsync(ct);var a=new List<ExpiringSubscription>();while(await r.ReadAsync(ct))a.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetFieldValue<DateOnly>(3),r.IsDBNull(4)?null:r.GetDecimal(4)));return a;}
    public async Task MarkSubscriptionRemindedAsync(int subscriptionId,CancellationToken ct){await using var c=dataSource.CreateCommand("UPDATE customer_subscriptions SET reminder_sent_at=now() WHERE id=$1 AND reminder_sent_at IS NULL");c.Parameters.AddWithValue(subscriptionId);await c.ExecuteNonQueryAsync(ct);}
    private async Task RequireInspectionForStatusAsync(int rentalId, string status, CancellationToken ct)
    {
        var type = status switch
        {
            "pre_inspection" or "renting" => "pre_rental",
            "post_inspection" or "completed" => "post_rental",
            _ => null
        };
        if (type is null) return;
        await using var command = dataSource.CreateCommand("SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND inspection_type=$2 AND ($3=false OR is_ok=true))");
        Add(command, rentalId, type, type == "pre_rental");
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new InvalidOperationException("The required inspection has not been recorded.");
    }

    public async Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT r.id,r.customer_id,r.vehicle_id,r.rental_type,r.status,r.is_subscription,
                   r.vat_price,r.deposit_price,r.overtime_price,r.total_price,
                   COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time)
            FROM rentals r
            LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
            WHERE r.id=$1 AND ($3 OR
                ($4 AND EXISTS(SELECT 1 FROM customers c JOIN users u ON u.id=c.user_id WHERE c.id=r.customer_id AND u.email=$2 AND u.is_active)) OR
                ($5 AND EXISTS(SELECT 1 FROM vehicle_assignments a JOIN staffs s ON s.id=a.driver_staff_id JOIN users u ON u.id=s.user_id
                    WHERE a.rental_id=r.id AND a.status='assigned' AND u.email=$2 AND u.is_active AND s.is_driver)))
            """);
        Add(command, id, email, readAll, customer, driver);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRental(reader) : null;
    }

    public async Task<Maintenance> SaveMaintenanceAsync(int? id,Maintenance x,CancellationToken ct){
        var q=id is null?"INSERT INTO vehicle_maintenance(vehicle_id,facility_id,assigned_staff_id,status,maintenance_type,scheduled_date,is_safe,description,cost)VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9) RETURNING id,vehicle_id,facility_id,assigned_staff_id,status,maintenance_type,scheduled_date,is_safe,description,cost":"UPDATE vehicle_maintenance SET facility_id=$1,assigned_staff_id=$2,status=$3,maintenance_type=$4,scheduled_date=$5,is_safe=$6,description=$7,cost=$8 WHERE id=$9 RETURNING id,vehicle_id,facility_id,assigned_staff_id,status,maintenance_type,scheduled_date,is_safe,description,cost";
        await using var c=dataSource.CreateCommand(q);
        if(id is null) Add(c,x.VehicleId,x.FacilityId,x.AssignedStaffId,x.Status,x.MaintenanceType,x.ScheduledDate,x.IsSafe,x.Description,x.Cost);
        else { Add(c,x.FacilityId,x.AssignedStaffId,x.Status,x.MaintenanceType,x.ScheduledDate,x.IsSafe,x.Description,x.Cost); c.Parameters.AddWithValue(id.Value); }
        await using var r=await c.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new KeyNotFoundException();
        return ReadMaintenance(r);
    }
    public async Task<IReadOnlyList<Maintenance>> ListMaintenanceAsync(int? vehicleId,string? status,CancellationToken ct){
        await using var c=dataSource.CreateCommand("SELECT id,vehicle_id,facility_id,assigned_staff_id,status,maintenance_type,scheduled_date,is_safe,description,cost FROM vehicle_maintenance WHERE ($1 IS NULL OR vehicle_id=$1) AND ($2 IS NULL OR status=$2) ORDER BY id DESC");
        Add(c,vehicleId,status);
        await using var r=await c.ExecuteReaderAsync(ct);
        var a=new List<Maintenance>();
        while(await r.ReadAsync(ct)) a.Add(ReadMaintenance(r));
        return a;
    }
    public async Task<object> DashboardAsync(DateTime? from,DateTime? to,CancellationToken ct){await using var c=dataSource.CreateCommand("SELECT COALESCE(SUM(amount) FILTER(WHERE status='paid'),0),COUNT(*) FILTER(WHERE status='renting'),(SELECT COUNT(*) FROM vehicle_maintenance WHERE status NOT IN ('completed','cancelled')), (SELECT COUNT(*) FROM customer_subscriptions WHERE status='active' AND end_date>=CURRENT_DATE) FROM invoices i FULL JOIN rentals r ON false");await using var rr=await c.ExecuteReaderAsync(ct);await rr.ReadAsync(ct);return new { revenue=rr.GetDecimal(0),renting=rr.GetInt64(1),maintenance=rr.GetInt64(2),activeSubscriptions=rr.GetInt64(3)};}
    private async Task<IReadOnlyList<OrganizationUnit>> UnitsAsync(string table,bool address,CancellationToken ct){await using var c=dataSource.CreateCommand($"SELECT id,name{(address?",address":"")} FROM {table} ORDER BY name");await using var r=await c.ExecuteReaderAsync(ct);var a=new List<OrganizationUnit>();while(await r.ReadAsync(ct))a.Add(new(r.GetInt32(0),r.GetString(1),address?S(r,2):null));return a;}
    private async Task<OrganizationUnit> SaveUnitAsync(string table,int? id,string name,string? address,CancellationToken ct){var q=id is null?(table=="facilities"?"INSERT INTO facilities(name,address)VALUES($1,$2) RETURNING id,name,address":"INSERT INTO departments(name)VALUES($1) RETURNING id,name"):(table=="facilities"?"UPDATE facilities SET name=$1,address=$2 WHERE id=$3 RETURNING id,name,address":"UPDATE departments SET name=$1 WHERE id=$2 RETURNING id,name");await using var c=dataSource.CreateCommand(q);Add(c,name);if(table=="facilities")c.Parameters.AddWithValue((object?)address??DBNull.Value);if(id is not null)c.Parameters.AddWithValue(id.Value);await using var r=await c.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new KeyNotFoundException();return new(r.GetInt32(0),r.GetString(1),r.FieldCount>2?S(r,2):null);}
    private async Task DeleteAsync(string table,int id,CancellationToken ct){await using var c=dataSource.CreateCommand($"DELETE FROM {table} WHERE id=$1");c.Parameters.AddWithValue(id);if(await c.ExecuteNonQueryAsync(ct)!=1)throw new KeyNotFoundException();}
    private static Vehicle ReadVehicle(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt32(3),S(r,4),r.GetString(5),S(r,6),S(r,7),S(r,8),r.IsDBNull(9)?null:r.GetInt32(9),r.IsDBNull(10)?null:r.GetDecimal(10),r.IsDBNull(11)?null:r.GetDecimal(11),S(r,12),S(r,13),S(r,14));
    private static RentalPolicy ReadPolicy(NpgsqlDataReader r)=>new(r.GetInt32(0),r.IsDBNull(1)?null:r.GetInt32(1),r.GetString(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5),r.GetBoolean(6));
    private static SubscriptionPlan ReadPlan(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetString(1),S(r,2),r.GetDecimal(3),r.GetInt32(4),r.IsDBNull(5)?null:r.GetDecimal(5),r.GetBoolean(6));
    private static CustomerSubscription ReadCustomerSubscription(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetFieldValue<DateOnly>(4),r.GetFieldValue<DateOnly>(5),r.IsDBNull(6)?null:r.GetDecimal(6),r.IsDBNull(7)?null:r.GetDecimal(7),r.GetString(8));
    private static Rental ReadRental(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.IsDBNull(10)?null:r.GetDateTime(10),r.IsDBNull(11)?null:r.GetDateTime(11));
    private static Maintenance ReadMaintenance(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.IsDBNull(2)?null:r.GetInt32(2),r.IsDBNull(3)?null:r.GetInt32(3),S(r,4),S(r,5),D(r,6),r.IsDBNull(7)?null:r.GetBoolean(7),S(r,8),r.IsDBNull(9)?null:r.GetDecimal(9));
    private async Task SaveVehicleDetailAsync(int vehicleId, VehicleDetail d, CancellationToken ct)
    {
        if (d.SeatCount.HasValue || d.Transmission is not null || d.TrunkCapacity.HasValue)
        {
            await using var cmd = dataSource.CreateCommand("INSERT INTO car_detail(vehicle_id,seat_count,transmission,trunk_capacity) VALUES($1,$2,$3,$4) ON CONFLICT(vehicle_id) DO UPDATE SET seat_count=$2,transmission=$3,trunk_capacity=$4");
            Add(cmd, vehicleId, d.SeatCount, d.Transmission, d.TrunkCapacity);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (d.BikeType is not null || d.HasStorageBox.HasValue)
        {
            await using var cmd = dataSource.CreateCommand("INSERT INTO motorbike_detail(vehicle_id,bike_type,has_storage_box) VALUES($1,$2,$3) ON CONFLICT(vehicle_id) DO UPDATE SET bike_type=$2,has_storage_box=$3");
            Add(cmd, vehicleId, d.BikeType, d.HasStorageBox);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (d.BatteryCapacity is not null || d.ChargingType is not null || d.MaxRangeKm.HasValue)
        {
            await using var cmd = dataSource.CreateCommand("INSERT INTO electric_car_detail(vehicle_id,battery_capacity,charging_type,charging_time_hours,max_range_km,battery_type) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(vehicle_id) DO UPDATE SET battery_capacity=$2,charging_type=$3,charging_time_hours=$4,max_range_km=$5,battery_type=$6");
            Add(cmd, vehicleId, d.BatteryCapacity, d.ChargingType, d.ChargingTimeHours, d.MaxRangeKm, d.BatteryType);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (d.GalleryImages is not null)
        {
            await using var del = dataSource.CreateCommand("DELETE FROM vehicle_imgs WHERE vehicle_id=$1");
            del.Parameters.AddWithValue(vehicleId);
            await del.ExecuteNonQueryAsync(ct);
            foreach (var img in d.GalleryImages)
            {
                if (string.IsNullOrWhiteSpace(img)) continue;
                await using var ins = dataSource.CreateCommand("INSERT INTO vehicle_imgs(vehicle_id,url) VALUES($1,$2)");
                Add(ins, vehicleId, img);
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
    }
    private async Task<VehicleDetail?> LoadVehicleDetailAsync(int vehicleId, CancellationToken ct)
    {
        int? seats = null; string? trans = null; decimal? trunk = null;
        string? bike = null; bool? box = null;
        string? batCap = null; string? chgType = null; string? chgHrs = null; decimal? range = null; string? batType = null;

        await using (var carCmd = dataSource.CreateCommand("SELECT seat_count,transmission,trunk_capacity FROM car_detail WHERE vehicle_id=$1"))
        {
            carCmd.Parameters.AddWithValue(vehicleId);
            await using var cr = await carCmd.ExecuteReaderAsync(ct);
            if (await cr.ReadAsync(ct)) { seats = cr.IsDBNull(0) ? null : cr.GetInt32(0); trans = S(cr, 1); trunk = cr.IsDBNull(2) ? null : cr.GetDecimal(2); }
        }
        await using (var bikeCmd = dataSource.CreateCommand("SELECT bike_type,has_storage_box FROM motorbike_detail WHERE vehicle_id=$1"))
        {
            bikeCmd.Parameters.AddWithValue(vehicleId);
            await using var br = await bikeCmd.ExecuteReaderAsync(ct);
            if (await br.ReadAsync(ct)) { bike = S(br, 0); box = br.IsDBNull(1) ? null : br.GetBoolean(1); }
        }
        await using (var elCmd = dataSource.CreateCommand("SELECT battery_capacity,charging_type,charging_time_hours,max_range_km,battery_type FROM electric_car_detail WHERE vehicle_id=$1"))
        {
            elCmd.Parameters.AddWithValue(vehicleId);
            await using var er = await elCmd.ExecuteReaderAsync(ct);
            if (await er.ReadAsync(ct)) { batCap = S(er, 0); chgType = S(er, 1); chgHrs = S(er, 2); range = er.IsDBNull(3) ? null : er.GetDecimal(3); batType = S(er, 4); }
        }
        var imgs = new List<string>();
        await using (var imgCmd = dataSource.CreateCommand("SELECT url FROM vehicle_imgs WHERE vehicle_id=$1 ORDER BY id"))
        {
            imgCmd.Parameters.AddWithValue(vehicleId);
            await using var ir = await imgCmd.ExecuteReaderAsync(ct);
            while (await ir.ReadAsync(ct)) imgs.Add(ir.GetString(0));
        }
        var hasAny = seats.HasValue || trans is not null || trunk.HasValue || bike is not null || box.HasValue || batCap is not null || imgs.Count > 0;
        return hasAny ? new(seats, trans, trunk, bike, box, batCap, chgType, chgHrs, range, batType, imgs) : null;
    }
    private static string? S(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i); private static DateOnly? D(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetFieldValue<DateOnly>(i);
    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
    private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object? value,CancellationToken ct)=>await ScalarAsync(c,t,sql,new[]{value},ct);private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object?[] values,CancellationToken ct){await using var x=new NpgsqlCommand(sql,c,t);Add(x,values);return await x.ExecuteScalarAsync(ct);}
}
