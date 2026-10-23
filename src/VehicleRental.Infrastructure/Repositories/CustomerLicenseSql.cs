namespace Car_Rental_System_BE.Infrastructure.Repositories;

internal static class CustomerLicenseSql
{
    // Expressions are developer-owned SQL identifiers/parameters, never user input.
    internal static string Eligible(string customerId, string vehicleId, string throughDate) => $"""
        EXISTS(SELECT 1 FROM customer_license_records cl JOIN vehicles lv ON lv.id={vehicleId}
            WHERE cl.customer_id={customerId} AND cl.status='approved'
            AND cl.expiry_date >= GREATEST(CURRENT_DATE,{throughDate})
            AND (cardinality(lv.allowed_license_types)=0 OR upper(btrim(cl.type))=ANY(lv.allowed_license_types)))
        """;
}
