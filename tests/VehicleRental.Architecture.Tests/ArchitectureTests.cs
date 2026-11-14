using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace VehicleRental.Architecture.Tests;

/// <summary>
/// Locks the one-way dependency chain Domain &lt;- Application &lt;- Infrastructure &lt;- WebApi.
/// </summary>
/// <remarks>
/// <para>
/// A layer is identified by its assembly and a role by the shape of the type (name, interface, base
/// class), never by a namespace or a folder path. Moving a module into a slice of its own — or, later,
/// under <c>Features/&lt;Module&gt;</c> — therefore cannot take code out of a rule's scope without a
/// compile error in this file.
/// </para>
/// <para>
/// Dependencies are read from IL, not from signatures: a type that only names another one inside a method
/// body still depends on it (see <see cref="IlScan"/>). Signature-only reflection cannot see that, and the
/// method body is exactly where a controller reaches into Infrastructure or one feature reaches into another.
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
        // Repository port declared in the Domain instead of the Application layer, implemented by
        // Infrastructure. Debt from the pre-refactor layout (see AGENTS.md "Application: Interfaces/").
        // Both ends moved into the Identity slice; the violation is the same one, only renamed.
        "Car_Rental_System_BE.Domain.Features.Identity.IUserRepository -> Car_Rental_System_BE.Infrastructure.Features.Identity.NpgsqlUserRepository",

        // ---------------------------------------------------------------------------------------
        // pre-existing, newly visible after the rules stopped depending on namespaces (Task A1)
        // ---------------------------------------------------------------------------------------
        // Empty by audit, not by omission: the shape-derived rules above see 22 Application repository
        // ports (12 of them outside Application.Interfaces.Repositories), 30 Application services and
        // every type of the Infrastructure assembly, and none of that newly in-scope code violates a
        // rule. Keep this group empty rather than filling it with anything but a proven violation.

        // ---------------------------------------------------------------------------------------
        // pre-existing, newly visible at IL level (Task C1)
        // ---------------------------------------------------------------------------------------
        // Empty by audit, not by omission. Reading method bodies adds 7 WebApi -> Infrastructure edges the
        // signature collector could not see (five constructed in WebApi.Extensions, one call in Program, both of
        // them the composition root this rule exempts). No controller, middleware or any other WebApi type gains
        // a violation once IL is read. Keep this group empty rather than filling it with anything but a proven
        // violation.
    ];

    private readonly ITestOutputHelper _output;

    public ArchitectureTests(ITestOutputHelper output) => _output = output;

    // Assemblies are loaded by name on purpose: an anchor type (typeof(X)) moves with the code it
    // anchors, so it would pin this file to a folder the refactor is about to change.
    private static readonly Assembly Domain = Layers.Domain;
    private static readonly Assembly Application = Layers.Application;
    private static readonly Assembly Infrastructure = Layers.Infrastructure;
    private static readonly Assembly WebApi = Layers.WebApi;

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

        var ports = RepositoryPortNames(Application).Concat(RepositoryPortNames(Domain)).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(ports);

        // Keeps the rule above from going vacuous: at least one port must actually reach Infrastructure.
        Assert.True(
            ports.Any(port => InfrastructureImplementers().Any(implementer => Interfaces(implementer).Contains(port))),
            "No Application repository port is implemented by an Infrastructure type: the rule inspects nothing.");

        AssertOnlyKnownViolations(ServiceViolations(services, ports));
    }

    /// <summary>
    /// WebApi is the presentation layer: controllers and middlewares may use Application contracts and
    /// the framework, but only the composition root (Program.cs and the Extensions registrations) may
    /// touch Infrastructure types.
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

    /// <summary>
    /// Guards the collector itself: the IL view must contain everything a signature-only view could see. The
    /// reverse is deliberately false — method bodies are the reason this rule set moved to IL.
    /// </summary>
    [Fact]
    public void Il_scan_sees_at_least_what_the_signatures_alone_reveal()
    {
        var missing = new List<string>();

        foreach (var assembly in new[] { Domain, Application, Infrastructure, WebApi })
        {
            var signatureEdges = 0;
            var ilEdges = 0;
            var signatureProductEdges = 0;
            var ilProductEdges = 0;

            foreach (var type in assembly.GetTypes().Where(IsAuthoredType))
            {
                var fromIlType = Layers.Il.Find(type.FullName!);
                Assert.True(fromIlType is not null, $"The IL scan does not see {type.FullName}, which reflection does.");

                var fromSignature = SignatureDependencies(type)
                    .Where(dependency => dependency != type.FullName)
                    .ToHashSet(StringComparer.Ordinal);
                var fromIl = fromIlType!.Dependencies.Concat(fromIlType.DeclaredInterfaces).ToHashSet(StringComparer.Ordinal);

                signatureEdges += fromSignature.Count;
                ilEdges += fromIl.Count;
                signatureProductEdges += fromSignature.Count(IsProductType);
                ilProductEdges += fromIl.Count(IsProductType);

                missing.AddRange(fromSignature
                    .Where(dependency => !fromIl.Contains(dependency))
                    .Select(dependency => $"{type.FullName} -> {dependency}"));
            }

            _output.WriteLine(
                $"{assembly.GetName().Name}: signature edges {signatureEdges}, IL edges {ilEdges} " +
                $"(+{ilEdges - signatureEdges}); of those, to Car_Rental_System_BE types: signature " +
                $"{signatureProductEdges}, IL {ilProductEdges} (+{ilProductEdges - signatureProductEdges})");
        }

        Assert.True(missing.Count == 0,
            "The IL scan missed dependencies the signature scan found. Fix the collector before trusting its rules:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    // ---------------------------------------------------------------------------------------------
    // Violations, one method per whitelist-guarded rule.
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> ControllerViolations()
    {
        var ports = RepositoryPortNames(Application).Concat(RepositoryPortNames(Domain)).ToHashSet(StringComparer.Ordinal);
        var infrastructureTypes = Layers.Of(Infrastructure).DeclaredTypeNames;

        return
        [
            .. Controllers()
                .SelectMany(controller => controller.Dependencies
                    .Where(dependency => ports.Contains(dependency) || infrastructureTypes.Contains(dependency))
                    .Select(dependency => $"{controller.Name} -> {dependency}")),
        ];
    }

    private static IReadOnlyList<string> ServiceViolations(IReadOnlyList<IlType> services, IReadOnlySet<string> ports) =>
    [
        .. services
            .SelectMany(service => Interfaces(service).Where(ports.Contains)
                .Select(port => $"{service.Name} -> {port}")),
    ];

    private static IReadOnlyList<string> WebApiToInfrastructureViolations()
    {
        var infrastructureTypes = Layers.Of(Infrastructure).DeclaredTypeNames;

        return
        [
            .. AuthoredTypes(WebApi)
                .Where(type => !IsWebApiCompositionRoot(type.Name))
                .SelectMany(type => type.Dependencies
                    .Where(infrastructureTypes.Contains)
                    .Select(dependency => $"{type.Name} -> {dependency}")),
        ];
    }

    private static IReadOnlyList<string> DomainPortImplementationViolations()
    {
        var implementers = InfrastructureImplementers();
        return
        [
            .. RepositoryPorts(Domain)
                .SelectMany(port => implementers
                    .Where(implementer => Interfaces(implementer).Contains(port.Name))
                    .Select(implementer => $"{port.Name} -> {implementer.Name}")),
        ];
    }

    /// <summary>Violations of every whitelist-guarded rule, recomputed from the compiled assemblies.</summary>
    private static IReadOnlyList<string> CurrentWhitelistedRuleViolations() =>
    [
        .. ControllerViolations(),
        .. ServiceViolations(
            ApplicationServices(),
            RepositoryPortNames(Application).Concat(RepositoryPortNames(Domain)).ToHashSet(StringComparer.Ordinal)),
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

    /// <summary>
    /// The WebApi composition root builds the Infrastructure layer, so it is the one place in WebApi allowed to
    /// name Infrastructure types: global <c>Program</c> and the <c>Extensions</c> namespace where the
    /// registrations moved in Phase 5.2. Nothing else qualifies — a controller or a middleware is not a root.
    /// </summary>
    private static bool IsWebApiCompositionRoot(string typeName) =>
        typeName == "Program" ||
        typeName == "Car_Rental_System_BE.WebApi.Extensions" ||
        typeName.StartsWith("Car_Rental_System_BE.WebApi.Extensions.", StringComparison.Ordinal);

    // ---------------------------------------------------------------------------------------------
    // Roles, derived from assembly + type shape. Nothing here may name a namespace or a folder.
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<IlType> AuthoredTypes(Assembly assembly) => Layers.Of(assembly).Types;

    /// <summary>Repository ports: interfaces named <c>I*Repository</c>, wherever they live in their layer.</summary>
    private static IReadOnlyList<IlType> RepositoryPorts(Assembly assembly) => Layers.Of(assembly).Types
        .Where(type => type.IsInterface &&
                       type.SimpleName.StartsWith('I') &&
                       type.SimpleName.EndsWith("Repository", StringComparison.Ordinal))
        .ToList();

    private static IReadOnlySet<string> RepositoryPortNames(Assembly assembly) =>
        RepositoryPorts(assembly).Select(port => port.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Application services: concrete <c>*Service</c> classes of the Application assembly that implement an
    /// Application interface, so a helper class that merely carries the suffix is not mistaken for a service.
    /// </summary>
    private static IReadOnlyList<IlType> ApplicationServices()
    {
        var applicationTypes = Layers.Of(Application).DeclaredTypeNames;
        return Layers.Of(Application).Types
            .Where(type => type.IsClass && !type.IsAbstract &&
                           type.SimpleName.EndsWith("Service", StringComparison.Ordinal) &&
                           Interfaces(type).Any(applicationTypes.Contains))
            .ToList();
    }

    private static IReadOnlyList<IlType> Controllers() => Layers.Of(WebApi).Types
        .Where(type => type.IsClass && !type.IsAbstract && DerivesFrom(type, "Microsoft.AspNetCore.Mvc.ControllerBase"))
        .ToList();

    private static IReadOnlyList<IlType> InfrastructureImplementers() => Layers.Of(Infrastructure).Types
        .Where(type => type.IsClass && !type.IsAbstract)
        .ToList();

    private static bool DerivesFrom(IlType type, string baseTypeName)
    {
        for (var current = type.BaseType; current is not null; current = Layers.Il.Find(current)?.BaseType)
            if (current == baseTypeName)
                return true;

        return false;
    }

    /// <summary>Every interface the type implements, including the ones it inherits.</summary>
    private static IReadOnlySet<string> Interfaces(IlType type) => Layers.Il.AllInterfaces(type);

    /// <summary>
    /// Program.cs is the composition root and the only WebApi code allowed to know Infrastructure, so it is not
    /// a role this file can hold to any rule. Compiler-generated types - including the compiler's own machinery
    /// nested under names that look hand-written, such as an array initializer blob - are not authored types.
    /// </summary>
    private static bool IsAuthoredType(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
            if (current.Name.Contains('<') ||
                current.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>(inherit: false) is not null)
                return false;

        return type.FullName is not null && !(type.Namespace is null && type.Name == "Program");
    }

    /// <summary>
    /// What a signature-only collector can see: base type, interfaces the type declares itself, members,
    /// parameters and return types. Kept to prove the IL scan is a superset of it, never used by a rule.
    /// </summary>
    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        if (type.BaseType is not null) yield return type.BaseType;
        // Interfaces inherited from a base type are that base type's dependency, not this type's.
        var inherited = type.BaseType?.GetInterfaces() ?? [];
        foreach (var implemented in type.GetInterfaces().Except(inherited)) yield return implemented;
        foreach (var field in type.GetFields(BindingFlags.DeclaredOnly | AllMembers)) yield return field.FieldType;
        foreach (var property in type.GetProperties(BindingFlags.DeclaredOnly | AllMembers)) yield return property.PropertyType;
        foreach (var constructor in type.GetConstructors(BindingFlags.DeclaredOnly | AllMembers))
            foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
        foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | AllMembers))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
        }
    }

    private static IEnumerable<string> SignatureDependencies(Type type) =>
        SignatureTypes(type)
            .SelectMany(UnwrapGenericArguments)
            .Select(SignatureName)
            .OfType<string>();

    /// <summary>
    /// The name the IL scan would report for this reflection type: element shapes collapse, a generic type
    /// reports its definition, and a compiler-generated type reports the authored type it belongs to.
    /// </summary>
    /// <summary>
    /// The name the IL scan would report for this reflection type: element shapes collapse, a generic type
    /// reports its definition, and a compiler-generated type reports the authored type it belongs to.
    /// </summary>
    private static string? SignatureName(Type type)
    {
        var current = type;
        while (current.HasElementType && current.GetElementType() is { } element) current = element;
        if (current.IsGenericType) current = current.GetGenericTypeDefinition();
        while (current.Name.Contains('<') && current.DeclaringType is { } declaring) current = declaring;

        return current.FullName;
    }

    private static bool IsProductType(string typeName) =>
        typeName.StartsWith("Car_Rental_System_BE.", StringComparison.Ordinal);

    private static IEnumerable<Type> UnwrapGenericArguments(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element)
            foreach (var unwrapped in UnwrapGenericArguments(element)) yield return unwrapped;
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                foreach (var unwrapped in UnwrapGenericArguments(argument)) yield return unwrapped;
        yield return type;
    }

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static bool IsExternalDependency(AssemblyName name) =>
        name.Name is "Npgsql" or "StackExchange.Redis" or "CloudinaryDotNet" ||
        name.Name!.StartsWith("Microsoft.AspNetCore") || name.Name.StartsWith("Microsoft.EntityFrameworkCore") || name.Name.StartsWith("Npgsql.EntityFrameworkCore");
}
