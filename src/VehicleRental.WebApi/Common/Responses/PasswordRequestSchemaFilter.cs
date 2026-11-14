using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Car_Rental_System_BE.WebApi.Common.Responses;

/// <summary>Swagger đọc property; MVC kiểm tra hợp lệ tham số constructor của record.</summary>
public sealed class PasswordRequestSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type.Name is "PasswordChangeStartRequest" or "ChangePasswordRequest")
        {
            foreach (var parameter in context.Type.GetConstructors().SelectMany(constructor => constructor.GetParameters()))
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!);
                if (!schema.Properties.TryGetValue(name, out var property)) continue;
                if (parameter.IsDefined(typeof(RequiredAttribute))) { schema.Required.Add(name); property.Nullable = false; }
                var pattern = parameter.GetCustomAttribute<RegularExpressionAttribute>();
                if (pattern is not null) property.Pattern = pattern.Pattern;
            }
        }
        foreach (var parameter in context.Type.GetConstructors().SelectMany(constructor => constructor.GetParameters()))
        {
            if (parameter.Name is not ("Password" or "NewPassword")) continue;
            var length = parameter.GetCustomAttribute<StringLengthAttribute>();
            if (length is null) continue;
            if (!schema.Properties.TryGetValue(JsonNamingPolicy.CamelCase.ConvertName(parameter.Name), out var property)) continue;
            property.MinLength = length.MinimumLength;
            property.MaxLength = length.MaximumLength;
        }
    }
}
