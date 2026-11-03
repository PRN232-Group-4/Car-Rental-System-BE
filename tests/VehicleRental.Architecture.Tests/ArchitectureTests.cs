using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Interfaces;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.WebApi.Controllers;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace VehicleRental.Architecture.Tests;

/// <summary>
/// Locks the one-way dependency chain Domain &lt;- Application &lt;- Infrastructure &lt;- WebApi
/// with plain reflection and no third-party test package.
/// </summary>
/// <remarks>
/// Every rule below is a hard rule. The only tolerated exceptions are listed one by one in
/// <see cref="KnownViolations"/>; the whitelist may only shrink. Adding an entry is a design
/// decision, not a test fix.
/// </remarks>
public sealed class ArchitectureTests
{
    private const string ApplicationRepositoriesNamespace = "Car_Rental_System_BE.Application.Interfaces.Repositories";
    private const string DomainInterfacesNamespace = "Car_Rental_System_BE.Domain.Interfaces";
    private const string InfrastructureNamespace = "Car_Rental_System_BE.Infrastructure";
    private const string ApplicationServicesNamespace = "Car_Rental_System_BE.Application.Services";

    /// <summary>
    /// Exact, fully-qualified pre-existing violations. Format: "offending type -&gt; offending dependency".
    /// No wildcards and no substring matching: a new violation of any kind fails the build.
    /// </summary>
    private static readonly string[] KnownViolations =
    [
        // WebApi middleware/ service reaches into an Infrastructure DTO: TokenOptions lives in
        // Infrastructure.Services.Authentication. Debt from the JWT/refresh-token split.
        "Car_Rental_System_BE.WebApi.Middlewares.Authentication.JwtTokenService -> Car_Rental_System_BE.Infrastructure.Services.Authentication.TokenOptions",
        // Repository port declared in the Domain instead of the Application layer, implemented by
        // Infrastructure. Debt from the pre-refactor layout (see AGENTS.md "Application: Interfaces/").
        "Car_Rental_System_BE.Domain.Interfaces.IUserRepository -> Car_Rental_System_BE.Infrastructure.Repositories.NpgsqlUserRepository",
    ];

    private static readonly Assembly Domain = typeof(User).Assembly;
    private static readonly Assembly Application = typeof(AccountService).Assembly;
    private static readonly Assembly Infrastructure = typeof(NpgsqlAccountRepository).Assembly;
    private static readonly Assembly WebApi = typeof(AccountController).Assembly;

    [Fact]
    public void Inner_layers_do_not_reference_outer_layers_or_persistence_drivers()
    {
        Assert.DoesNotContain(Domain.GetReferencedAssemblies(), name =>
            name.Name!.StartsWith("VehicleRental.") || IsExternalDependency(name));
        Assert.DoesNotContain(Application.GetReferencedAssemblies(), name =>
            name.Name is "VehicleRental.Infrastructure" or "VehicleRental.WebApi" || IsExternalDependency(name));
    }

    [Fact]
    public void Controllers_depend_on_services_instead_of_repository_or_infrastructure_types()
    {
        var controllers = WebApi.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type));
        foreach (var type in controllers)
        {
            var dependencies = type.GetConstructors().SelectMany(ctor => ctor.GetParameters());
            Assert.DoesNotContain(dependencies, parameter =>
                parameter.ParameterType.Namespace?.StartsWith(ApplicationRepositoriesNamespace) == true ||
                parameter.ParameterType.Namespace?.StartsWith(DomainInterfacesNamespace) == true ||
                parameter.ParameterType.Namespace?.StartsWith(InfrastructureNamespace) == true);
        }
    }

    [Fact]
    public void Services_do_not_implement_repository_interfaces()
    {
        var repositories = Application.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == ApplicationRepositoriesNamespace)
            .Concat(Domain.GetTypes().Where(type => type.IsInterface && type.Namespace == DomainInterfacesNamespace)).ToHashSet();
        var services = Application.GetTypes().Where(type => type.Namespace == ApplicationServicesNamespace);
        foreach (var service in services)
            Assert.DoesNotContain(service.GetInterfaces(), repositories.Contains);
        Assert.Contains(typeof(IAccountRepository), typeof(NpgsqlAccountRepository).GetInterfaces());
    }

    /// <summary>
    /// WebApi is the presentation layer: controllers and middlewares may use Application contracts and
    /// the framework, but only the composition root (Program.cs) may touch Infrastructure types.
    /// </summary>
    [Fact]
    public void WebApi_types_do_not_depend_on_infrastructure_types()
    {
        var violations = new List<string>();
        foreach (var type in WebApi.GetTypes().Where(IsAuthoredType))
        {
            foreach (var dependency in ReferencedTypes(type).SelectMany(UnwrapGenericArguments).Distinct())
            {
                if (dependency.Namespace?.StartsWith(InfrastructureNamespace) != true) continue;
                violations.Add($"{type.FullName} -> {dependency.FullName}");
            }
        }

        AssertOnlyKnownViolations(violations);
    }

    /// <summary>
    /// Persistence ports belong to the Application layer. A Domain interface implemented by an
    /// Infrastructure type couples the domain model to a storage concern.
    /// </summary>
    [Fact]
    public void Domain_interfaces_are_not_implemented_by_infrastructure_types()
    {
        var implementers = Infrastructure.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .ToList();
        var violations = Domain.GetTypes()
            .Where(type => type.IsInterface)
            .SelectMany(port => implementers
                .Where(implementer => implementer.GetInterfaces().Contains(port))
                .Select(implementer => $"{port.FullName} -> {implementer.FullName}"))
            .ToList();

        AssertOnlyKnownViolations(violations);
    }

    /// <summary>Guards the whitelist from outliving the code it describes: it may only shrink.</summary>
    [Fact]
    public void Known_violations_are_all_still_violations()
    {
        var current = CurrentWhitelistedRuleViolations();
        var stale = KnownViolations.Except(current, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0,
            "These whitelist entries no longer describe a real violation. Delete them: the whitelist may only shrink." +
            Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    private static void AssertOnlyKnownViolations(IEnumerable<string> violations)
    {
        var unexpected = violations.Except(KnownViolations, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(unexpected.Count == 0,
            "New architecture violation(s) detected. Fix the dependency instead of extending KnownViolations " +
            "(the whitelist may only shrink):" + Environment.NewLine + string.Join(Environment.NewLine, unexpected));
    }

    /// <summary>Violations of the two whitelist-guarded rules, recomputed from the compiled assemblies.</summary>
    private static IReadOnlyList<string> CurrentWhitelistedRuleViolations() =>
    [
        .. WebApi.GetTypes().Where(IsAuthoredType)
            .SelectMany(type => ReferencedTypes(type).SelectMany(UnwrapGenericArguments)
                .Where(dependency => dependency.Namespace?.StartsWith(InfrastructureNamespace) == true)
                .Select(dependency => $"{type.FullName} -> {dependency.FullName}")),
        .. Domain.GetTypes().Where(type => type.IsInterface)
            .SelectMany(port => Infrastructure.GetTypes()
                .Where(implementer => !implementer.IsAbstract && !implementer.IsInterface && implementer.GetInterfaces().Contains(port))
                .Select(implementer => $"{port.FullName} -> {implementer.FullName}")),
    ];

    /// <summary>Program.cs is the composition root and the only WebApi code allowed to know Infrastructure.</summary>
    private static bool IsAuthoredType(Type type) =>
        type.FullName is not null &&
        !(type.Namespace is null && type.Name == "Program") &&
        !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) &&
        !type.Name.Contains('<');

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        if (type.BaseType is not null) yield return type.BaseType;
        foreach (var implemented in type.GetInterfaces()) yield return implemented;
        foreach (var field in type.GetFields(DeclaredMembers)) yield return field.FieldType;
        foreach (var property in type.GetProperties(DeclaredMembers)) yield return property.PropertyType;
        foreach (var constructor in type.GetConstructors(DeclaredMembers))
            foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
        foreach (var method in type.GetMethods(DeclaredMembers))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
        }
    }

    private static IEnumerable<Type> UnwrapGenericArguments(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element)
            foreach (var unwrapped in UnwrapGenericArguments(element)) yield return unwrapped;
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                foreach (var unwrapped in UnwrapGenericArguments(argument)) yield return unwrapped;
        yield return type;
    }

    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static bool IsExternalDependency(AssemblyName name) =>
        name.Name is "Npgsql" or "StackExchange.Redis" or "CloudinaryDotNet" ||
        name.Name!.StartsWith("Microsoft.AspNetCore") || name.Name.StartsWith("Microsoft.EntityFrameworkCore") || name.Name.StartsWith("Npgsql.EntityFrameworkCore");
}
