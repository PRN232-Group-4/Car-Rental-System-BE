using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>Swagger reads properties; MVC validates record constructor parameters.</summary>
public sealed class PasswordRequestSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
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
