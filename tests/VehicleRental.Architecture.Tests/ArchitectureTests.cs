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
/// <para>
/// A layer is identified by its assembly and a role by the shape of the type (name, interface, base
/// class), never by a namespace or a folder path. Moving a module into a slice of its own — or, later,
/// under <c>Features/&lt;Module&gt;</c> — therefore cannot take code out of a rule's scope without a
/// compile error in this file.
/// </para>
/// <para>
/// Every rule below is a hard rule. The only tolerated exceptions are listed one by one in
/// <see cref="KnownViolations"/>; the whitelist may only shrink. Adding an entry is a design
/// decision, not a test fix.
/// </para>
/// </remarks>
public sealed class ArchitectureTests
{
    /// <summary>
    /// Exact, fully-qualified pre-existing violations of a whitelist-guarded rule.
    /// Format: "offending type -&gt; offending dependency".
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

        // ---------------------------------------------------------------------------------------
        // pre-existing, newly visible after the rules stopped depending on namespaces (Task A1)
        // ---------------------------------------------------------------------------------------
        // Empty by audit, not by omission: the shape-derived rules above see 22 Application repository
        // ports (12 of them outside Application.Interfaces.Repositories), 30 Application services and
        // every type of the Infrastructure assembly, and none of that newly in-scope code violates a
        // rule. Keep this group empty rather than filling it with anything but a proven violation.
    ];

    // Assemblies are loaded by name on purpose: an anchor type (typeof(X)) moves with the code it
    // anchors, so it would pin this file to a folder the refactor is about to change.
    private static readonly Assembly Domain = Project("VehicleRental.Domain");
    private static readonly Assembly Application = Project("VehicleRental.Application");
    private static readonly Assembly Infrastructure = Project("VehicleRental.Infrastructure");
    private static readonly Assembly WebApi = Project("VehicleRental.WebApi");

    [Fact]
    public void Inner_layers_do_not_reference_outer_layers_or_persistence_drivers()
    {
        Assert.DoesNotContain(Domain.GetReferencedAssemblies(), name =>
            name.Name!.StartsWith("VehicleRental.") || IsExternalDependency(name));
        Assert.DoesNotContain(Application.GetReferencedAssemblies(), name =>
            name.Name is "VehicleRental.Infrastructure" or "VehicleRental.WebApi" || IsExternalDependency(name));
    }

    /// <summary>
    /// Controllers are the presentation edge: they may depend on Application contracts and the framework,
    /// but never on a persistence port or on an Infrastructure type.
    /// </summary>
    [Fact]
    public void Controllers_depend_on_services_instead_of_repository_or_infrastructure_types()
    {
        Assert.NotEmpty(Controllers());
        Assert.NotEmpty(RepositoryPorts(Application));
        AssertOnlyKnownViolations(ControllerViolations());
    }

    /// <summary>
    /// Services orchestrate ports; they never are one. A service that implements a repository port blurs
    /// the boundary the Application layer exists to draw.
    /// </summary>
    [Fact]
    public void Services_do_not_implement_repository_interfaces()
    {
        var services = ApplicationServices();
        Assert.NotEmpty(services);

        var ports = RepositoryPorts(Application).Concat(RepositoryPorts(Domain)).ToHashSet();
        Assert.NotEmpty(ports);

        // Keeps the rule above from going vacuous: at least one port must actually reach Infrastructure.
        Assert.True(
            ports.Any(port => InfrastructureImplementers().Any(implementer => implementer.GetInterfaces().Contains(port))),
            "No Application repository port is implemented by an Infrastructure type: the rule inspects nothing.");

        AssertOnlyKnownViolations(ServiceViolations(services, ports));
    }

    /// <summary>
    /// WebApi is the presentation layer: controllers and middlewares may use Application contracts and
    /// the framework, but only the composition root (Program.cs) may touch Infrastructure types.
    /// </summary>
    [Fact]
    public void WebApi_types_do_not_depend_on_infrastructure_types() =>
        AssertOnlyKnownViolations(WebApiToInfrastructureViolations());

    /// <summary>
    /// Persistence ports belong to the Application layer. A Domain repository port implemented by an
    /// Infrastructure type couples the domain model to a storage concern.
    /// </summary>
    [Fact]
    public void Domain_repository_ports_are_not_implemented_by_infrastructure_types()
    {
        Assert.NotEmpty(RepositoryPorts(Domain));
        AssertOnlyKnownViolations(DomainPortImplementationViolations());
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

    // ---------------------------------------------------------------------------------------------
    // Violations, one method per whitelist-guarded rule.
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> ControllerViolations()
    {
        var ports = RepositoryPorts(Application).Concat(RepositoryPorts(Domain)).ToHashSet();
        return
        [
            .. Controllers()
                .SelectMany(controller => controller.GetConstructors(DeclaredMembers)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType)
                    .SelectMany(UnwrapGenericArguments)
                    .Where(dependency => ports.Contains(dependency) || dependency.Assembly == Infrastructure)
                    .Select(dependency => $"{controller.FullName} -> {dependency.FullName}")),
        ];
    }

    private static IReadOnlyList<string> ServiceViolations(IReadOnlyList<Type> services, IReadOnlySet<Type> ports) =>
    [
        .. services
            .Where(service => service.GetInterfaces().Any(ports.Contains))
            .SelectMany(service => service.GetInterfaces().Where(ports.Contains)
                .Select(port => $"{service.FullName} -> {port.FullName}")),
    ];

    private static IReadOnlyList<string> WebApiToInfrastructureViolations() =>
    [
        .. AuthoredTypes(WebApi)
            .SelectMany(type => ReferencedTypes(type).SelectMany(UnwrapGenericArguments)
                .Where(dependency => dependency.Assembly == Infrastructure)
                .Select(dependency => $"{type.FullName} -> {dependency.FullName}")),
    ];

    private static IReadOnlyList<string> DomainPortImplementationViolations()
    {
        var implementers = InfrastructureImplementers();
        return
        [
            .. RepositoryPorts(Domain)
                .SelectMany(port => implementers
                    .Where(implementer => implementer.GetInterfaces().Contains(port))
                    .Select(implementer => $"{port.FullName} -> {implementer.FullName}")),
        ];
    }

    /// <summary>Violations of every whitelist-guarded rule, recomputed from the compiled assemblies.</summary>
    private static IReadOnlyList<string> CurrentWhitelistedRuleViolations() =>
    [
        .. ControllerViolations(),
        .. ServiceViolations(ApplicationServices(), RepositoryPorts(Application).Concat(RepositoryPorts(Domain)).ToHashSet()),
        .. WebApiToInfrastructureViolations(),
        .. DomainPortImplementationViolations(),
    ];

    private static void AssertOnlyKnownViolations(IEnumerable<string> violations)
    {
        var unexpected = violations.Distinct(StringComparer.Ordinal).Except(KnownViolations, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToList();

        Assert.True(unexpected.Count == 0,
            "New architecture violation(s) detected. Fix the dependency instead of extending KnownViolations " +
            "(the whitelist may only shrink):" + Environment.NewLine + string.Join(Environment.NewLine, unexpected));
    }

    // ---------------------------------------------------------------------------------------------
    // Roles, derived from assembly + type shape. Nothing here may name a namespace or a folder.
    // ---------------------------------------------------------------------------------------------

    private static Assembly Project(string simpleName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(simpleName));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Cannot load {simpleName}. The architecture test project must keep a ProjectReference to it " +
                "so the assembly reaches the test output.", exception);
        }
    }

    private static IReadOnlyList<Type> AuthoredTypes(Assembly assembly) =>
        assembly.GetTypes().Where(IsAuthoredType).ToList();

    /// <summary>Repository ports: interfaces named <c>I*Repository</c>, wherever they live in their layer.</summary>
    private static IReadOnlyList<Type> RepositoryPorts(Assembly assembly) => assembly.GetTypes()
        .Where(type => type.IsInterface && type.Name.StartsWith('I') && type.Name.EndsWith("Repository", StringComparison.Ordinal))
        .ToList();

    /// <summary>
    /// Application services: concrete <c>*Service</c> classes of the Application assembly that implement an
    /// Application interface, so a helper class that merely carries the suffix is not mistaken for a service.
    /// </summary>
    private static IReadOnlyList<Type> ApplicationServices() => Application.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract && IsAuthoredType(type) &&
                       type.Name.EndsWith("Service", StringComparison.Ordinal) &&
                       type.GetInterfaces().Any(@interface => @interface.Assembly == Application))
        .ToList();

    private static IReadOnlyList<Type> Controllers() => AuthoredTypes(WebApi)
        .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
        .ToList();

    private static IReadOnlyList<Type> InfrastructureImplementers() => Infrastructure.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract)
        .ToList();

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
