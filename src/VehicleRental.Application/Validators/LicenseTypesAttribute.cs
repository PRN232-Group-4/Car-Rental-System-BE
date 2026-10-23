using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Car_Rental_System_BE.Application.Validators;

public sealed class LicenseTypesAttribute : ValidationAttribute
{
    public LicenseTypesAttribute() : base("Nhập tối đa 30 hạng GPLX, mỗi hạng gồm 1–20 chữ cái hoặc chữ số.") { }

    public override bool IsValid(object? value) => value is null || value is IReadOnlyList<string> types
        && types.Count <= 30 && types.All(x => x is not null && Regex.IsMatch(x.Trim(), "^[A-Za-z0-9]{1,20}$"));

    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? types)
    {
        if (!new LicenseTypesAttribute().IsValid(types))
            throw new ArgumentException(new LicenseTypesAttribute().ErrorMessage, "AllowedLicenseTypes");
        return types?.Select(x => x.Trim().ToUpperInvariant()).Distinct().ToArray() ?? [];
    }
}
