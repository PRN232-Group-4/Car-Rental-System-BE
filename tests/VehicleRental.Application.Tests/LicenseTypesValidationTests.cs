using Xunit;
using Car_Rental_System_BE.Application.Features.Fleet;

namespace VehicleRental.Tests;

public class LicenseTypesValidationTests
{
    [Fact]
    public void Normalizes_case_whitespace_and_duplicates_without_inventing_hierarchy()
    {
        Assert.Equal(new[] { "B", "A1", "B2" }, LicenseTypesAttribute.Normalize([" b ", "A1", "B", "b2"]));
        Assert.Empty(LicenseTypesAttribute.Normalize([]));
        Assert.Empty(LicenseTypesAttribute.Normalize(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("B,A1")]
    [InlineData("B-1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void Invalid_class_has_actionable_validation(string value)
    {
        Assert.False(new LicenseTypesAttribute().IsValid(new[] { value }));
        var error = Assert.Throws<ArgumentException>(() => LicenseTypesAttribute.Normalize([value]));
        Assert.Equal("AllowedLicenseTypes", error.ParamName);
    }

    [Fact]
    public void Rejects_null_elements_and_excessive_count()
    {
        Assert.False(new LicenseTypesAttribute().IsValid(new string[] { null! }));
        Assert.False(new LicenseTypesAttribute().IsValid(Enumerable.Repeat("B", 31).ToArray()));
    }
}
