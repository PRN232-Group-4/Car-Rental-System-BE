using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;

namespace VehicleRental.Architecture.Tests;

/// <summary>
/// The four source assemblies, loaded by simple name, plus the IL scan of what each of their types depends on.
/// </summary>
/// <remarks>
/// Assemblies are loaded by name on purpose: an anchor type (typeof(X)) moves with the code it anchors, so it
/// would pin the rules to a folder the refactor is about to change.
/// </remarks>
internal static class Layers
{
    internal static readonly Assembly Domain = Load("VehicleRental.Domain");
    internal static readonly Assembly Application = Load("VehicleRental.Application");
    internal static readonly Assembly Infrastructure = Load("VehicleRental.Infrastructure");
    internal static readonly Assembly WebApi = Load("VehicleRental.WebApi");

    /// <summary>
    /// Every dependency of every authored type, read from the compiled metadata and IL - so a type named only
    /// inside a method body, an attribute or a generic argument counts as a dependency. Tests are not scanned.
    /// </summary>
    internal static readonly IlScan Il = IlScan.Load(Domain, Application, Infrastructure, WebApi);

    internal static IlAssemblyScan Of(Assembly assembly) => Il.Assembly(assembly.GetName().Name!);

    private static Assembly Load(string simpleName)
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
}

/// <summary>One authored type of a scanned assembly and the types it depends on.</summary>
/// <param name="Name">Reflection-compatible full name: <c>Namespace.Type</c>, nested types joined with <c>+</c>.</param>
/// <param name="Namespace">Namespace of the outermost declaring type (nested types are not namespace-less here).</param>
/// <param name="SimpleName">Name without namespace - what <see cref="Type.Name"/> would report.</param>
/// <param name="BaseType">Full name of the base type, if any.</param>
/// <param name="DeclaredInterfaces">
/// Interfaces declared on the type itself. Inherited ones are resolved by <see cref="IlScan.AllInterfaces"/>.
/// </param>
/// <param name="Dependencies">
/// Every type this one depends on: base type, interfaces, fields, properties, events, attributes, generic
/// argument and the operands of every instruction in every method body.
/// </param>
internal sealed record IlType(
    string Name,
    string Namespace,
    string SimpleName,
    bool IsInterface,
    bool IsAbstract,
    bool IsClass,
    string? BaseType,
    IReadOnlySet<string> DeclaredInterfaces,
    IReadOnlySet<string> Dependencies);

/// <summary>The authored types of one assembly, as IL sees them.</summary>
internal sealed class IlAssemblyScan
{
    internal IlAssemblyScan(string name, IReadOnlyList<IlType> types, IReadOnlySet<string> declaredTypeNames)
    {
        Name = name;
        Types = types;
        DeclaredTypeNames = declaredTypeNames;
    }

    internal string Name { get; }

    /// <summary>Authored types only: compiler-generated types (closures, state machines, local functions) are
    /// attributed to the type that authored them, so reported names stay stable across compiles.</summary>
    internal IReadOnlyList<IlType> Types { get; }

    /// <summary>
    /// Names of everything the assembly declares, authored or compiler-generated (the latter as their authored
    /// owner). Used to answer "is this dependency ours?" without guessing from a namespace.
    /// </summary>
    internal IReadOnlySet<string> DeclaredTypeNames { get; }
}

/// <summary>
/// Dependencies collected from IL via Mono.Cecil (package Mono.Cecil 0.11.6, MIT).
/// </summary>
/// <remarks>
/// <para>
/// Why not reflection alone: reflection only shows a type's signature - base type, interfaces, fields,
/// properties, parameters and return types. A type that news up, calls or otherwise names another type only
/// inside a method body is invisible to it, so a controller could reach into Infrastructure (or one feature into
/// another) and every rule would stay green. IL has no such blind spot: method bodies, attributes, local
/// variable types and generic arguments are all metadata we can read.
/// </para>
/// <para>
/// Compiler-generated nested types (lambdas, async/iterator state machines, local functions) depend on whatever
/// the author's method depended on. Their edges are attributed to the nearest authored declaring type so a
/// violation is reported once, under a name a human can find in the source.
/// </para>
/// </remarks>
internal sealed class IlScan
{
    private readonly Dictionary<string, IlAssemblyScan> _assemblies;
    private readonly Dictionary<string, IlType> _types;

    private IlScan(Dictionary<string, IlAssemblyScan> assemblies, Dictionary<string, IlType> types)
    {
        _assemblies = assemblies;
        _types = types;
    }

    internal static IlScan Load(params Assembly[] assemblies)
    {
        var scans = assemblies.Select(Read).ToList();

        // Types are keyed by full name, so a name declared twice would silently unify two different types. Fail
        // loudly instead: it would make every rule that walks a base chain or an interface list lie.
        var types = new Dictionary<string, IlType>(StringComparer.Ordinal);
        foreach (var type in scans.SelectMany(scan => scan.Types))
            if (!types.TryAdd(type.Name, type))
                throw new InvalidOperationException(
                    $"{type.Name} is declared by more than one scanned assembly; the dependency scan keys types by name.");

        return new IlScan(scans.ToDictionary(scan => scan.Name, StringComparer.Ordinal), types);
    }

    internal IlAssemblyScan Assembly(string simpleName) => _assemblies[simpleName];

    internal IlType? Find(string typeName) => _types.GetValueOrDefault(typeName);

    /// <summary>The type plus every interface it implements, directly or through a base type or another interface.</summary>
    internal IReadOnlySet<string> AllInterfaces(IlType type)
    {
        var interfaces = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(type.DeclaredInterfaces);

        for (var current = type.BaseType; current is not null && _types.TryGetValue(current, out var baseType); current = baseType.BaseType)
            foreach (var inherited in baseType.DeclaredInterfaces) pending.Enqueue(inherited);

        while (pending.Count > 0)
        {
            var candidate = pending.Dequeue();
            if (!interfaces.Add(candidate)) continue;
            if (_types.TryGetValue(candidate, out var declaration))
                foreach (var inherited in declaration.DeclaredInterfaces) pending.Enqueue(inherited);
        }

        return interfaces;
    }

    // -----------------------------------------------------------------------------------------
    // Reading one assembly. Everything below is metadata-only: no type is loaded or instantiated.
    // -----------------------------------------------------------------------------------------

    private static IlAssemblyScan Read(Assembly assembly)
    {
        using var module = AssemblyDefinition.ReadAssembly(
            assembly.Location, new ReaderParameters { InMemory = true, ReadSymbols = false });

        var dependenciesByOwner = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var declarations = new HashSet<string>(StringComparer.Ordinal);
        var authored = new List<IlType>();

        foreach (var type in AllTypes(module.MainModule))
        {
            var name = FullName(type);
            declarations.Add(name);

            // A top-level compiler-generated type (an anonymous type, a display class at namespace level) has no
            // author to report against, so it is skipped - its edges are covered by whoever uses it.
            var owner = AuthoredOwner(type);
            if (owner is null) continue;

            var ownerName = FullName(owner);
            declarations.Add(ownerName);
            if (!dependenciesByOwner.TryGetValue(ownerName, out var ownerDependencies))
                dependenciesByOwner[ownerName] = ownerDependencies = new HashSet<string>(StringComparer.Ordinal);

            foreach (var dependency in DependenciesOf(type))
                if (dependency is not null && dependency != ownerName)
                    ownerDependencies.Add(dependency);
        }

        foreach (var type in AllTypes(module.MainModule))
        {
            var owner = AuthoredOwner(type);
            if (owner is null || !ReferenceEquals(owner, type)) continue;

            var name = FullName(type);
            authored.Add(new IlType(
                Name: name,
                Namespace: NamespaceOf(type),
                SimpleName: type.Name,
                IsInterface: type.IsInterface,
                IsAbstract: type.IsAbstract,
                IsClass: type.IsClass,
                BaseType: type.BaseType is null ? null : FullName(type.BaseType),
                DeclaredInterfaces: type.Interfaces
                    .Select(implementation => FullName(implementation.InterfaceType))
                    .ToHashSet(StringComparer.Ordinal),
                Dependencies: dependenciesByOwner.TryGetValue(name, out var dependencies)
                    ? dependencies
                    : new HashSet<string>(StringComparer.Ordinal)));
        }

        return new IlAssemblyScan(assembly.GetName().Name!, authored, declarations);
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
            foreach (var flattened in WithNested(type))
                yield return flattened;
    }

    private static IEnumerable<TypeDefinition> WithNested(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes)
            foreach (var flattened in WithNested(nested))
                yield return flattened;
    }

    /// <summary>
    /// The type a compiler-generated type was written for: closures, state machines and local functions are
    /// nested inside the type whose method produced them, so walking <c>DeclaringType</c> up to the first
    /// authored type gives the line a reviewer can act on.
    /// </summary>
    private static TypeDefinition? AuthoredOwner(TypeDefinition type)
    {
        var current = type;
        while (IsCompilerGenerated(current))
        {
            if (current.DeclaringType is null) return null;
            current = current.DeclaringType;
        }

        return current;
    }

    private static bool IsCompilerGenerated(TypeDefinition type)
    {
        // A nested type inside a compiler-generated type is compiler-generated too, whatever it is called:
        // the compiler nests its own machinery (an enumerator inside the read-only list a collection expression
        // builds) under names that look hand-written.
        for (var current = type; current is not null; current = current.DeclaringType)
            if (current.Name.Contains('<', StringComparison.Ordinal) ||
                current.CustomAttributes.Any(attribute =>
                    attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
                return true;

        return false;
    }

    private static IEnumerable<string?> DependenciesOf(TypeDefinition type)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);

        CollectType(type.BaseType, dependencies);
        foreach (var implementation in type.Interfaces)
            CollectType(implementation.InterfaceType, dependencies);
        CollectAttributes(type, dependencies);
        CollectGenericParameters(type.GenericParameters, dependencies);

        foreach (var field in type.Fields)
        {
            CollectType(field.FieldType, dependencies);
            CollectAttributes(field, dependencies);
        }

        foreach (var property in type.Properties)
        {
            CollectType(property.PropertyType, dependencies);
            CollectAttributes(property, dependencies);
            foreach (var parameter in property.Parameters) CollectType(parameter.ParameterType, dependencies);
        }

        foreach (var @event in type.Events)
        {
            CollectType(@event.EventType, dependencies);
            CollectAttributes(@event, dependencies);
        }

        foreach (var method in type.Methods)
            CollectMethod(method, dependencies);

        return dependencies;
    }

    private static void CollectMethod(MethodDefinition method, HashSet<string> dependencies)
    {
        CollectType(method.ReturnType, dependencies);
        CollectAttributes(method.MethodReturnType, dependencies);
        CollectAttributes(method, dependencies);
        CollectGenericParameters(method.GenericParameters, dependencies);

        foreach (var parameter in method.Parameters)
        {
            CollectType(parameter.ParameterType, dependencies);
            CollectAttributes(parameter, dependencies);
        }

        // An explicit interface implementation or an override names the method it binds to.
        foreach (var @override in method.Overrides) CollectMethodReference(@override, dependencies);

        if (!method.HasBody) return;

        var body = method.Body;
        foreach (var variable in body.Variables) CollectType(variable.VariableType, dependencies);
        foreach (var handler in body.ExceptionHandlers) CollectType(handler.CatchType, dependencies);

        // The point of the whole exercise: every type named by an instruction of the method body.
        foreach (var instruction in body.Instructions) CollectOperand(instruction.Operand, dependencies);
    }

    private static void CollectOperand(object? operand, HashSet<string> dependencies)
    {
        switch (operand)
        {
            case TypeReference type:
                CollectType(type, dependencies);
                break;
            case MethodReference method:
                CollectMethodReference(method, dependencies);
                break;
            case FieldReference field:
                CollectType(field.DeclaringType, dependencies);
                CollectType(field.FieldType, dependencies);
                break;
            case CallSite callSite:
                CollectType(callSite.ReturnType, dependencies);
                foreach (var parameter in callSite.Parameters) CollectType(parameter.ParameterType, dependencies);
                break;
            case VariableDefinition variable:
                CollectType(variable.VariableType, dependencies);
                break;
            case ParameterDefinition parameter:
                CollectType(parameter.ParameterType, dependencies);
                break;
        }
    }

    private static void CollectMethodReference(MethodReference method, HashSet<string> dependencies)
    {
        CollectType(method.DeclaringType, dependencies);
        CollectType(method.ReturnType, dependencies);
        foreach (var parameter in method.Parameters) CollectType(parameter.ParameterType, dependencies);
        if (method is GenericInstanceMethod closed)
            foreach (var argument in closed.GenericArguments) CollectType(argument, dependencies);
    }

    private static void CollectAttributes(ICustomAttributeProvider provider, HashSet<string> dependencies)
    {
        if (!provider.HasCustomAttributes) return;

        foreach (var attribute in provider.CustomAttributes)
        {
            CollectType(attribute.AttributeType, dependencies);
            foreach (var argument in attribute.ConstructorArguments) CollectAttributeArgument(argument, dependencies);
            foreach (var property in attribute.Properties) CollectAttributeArgument(property.Argument, dependencies);
            foreach (var field in attribute.Fields) CollectAttributeArgument(field.Argument, dependencies);
        }
    }

    private static void CollectAttributeArgument(CustomAttributeArgument argument, HashSet<string> dependencies)
    {
        if (argument.Value is TypeReference type) CollectType(type, dependencies);
        if (argument.Value is CustomAttributeArgument[] array)
            foreach (var element in array) CollectAttributeArgument(element, dependencies);
    }

    private static void CollectGenericParameters(IEnumerable<GenericParameter> parameters, HashSet<string> dependencies)
    {
        foreach (var parameter in parameters)
        {
            CollectType(parameter, dependencies);
            CollectAttributes(parameter, dependencies);
            foreach (var constraint in parameter.Constraints) CollectType(constraint.ConstraintType, dependencies);
        }
    }

    /// <summary>
    /// Adds the referenced type and, for a generic or modified type, its arguments as well: depending on
    /// <c>IRepository&lt;Vehicle&gt;</c> is a dependency on both types, not on neither.
    /// </summary>
    private static void CollectType(TypeReference? type, HashSet<string> dependencies)
    {
        switch (type)
        {
            case null:
            case GenericParameter:
                return;
            case GenericInstanceType closed:
                CollectType(closed.ElementType, dependencies);
                foreach (var argument in closed.GenericArguments) CollectType(argument, dependencies);
                return;
            case FunctionPointerType pointer:
                CollectType(pointer.ReturnType, dependencies);
                foreach (var parameter in pointer.Parameters) CollectType(parameter.ParameterType, dependencies);
                return;
            case TypeSpecification specification:
                CollectType(specification.ElementType, dependencies);
                if (specification is RequiredModifierType { ModifierType: { } required }) CollectType(required, dependencies);
                if (specification is OptionalModifierType { ModifierType: { } optional }) CollectType(optional, dependencies);
                return;
        }

        if (DependencyName(type) is { } name) dependencies.Add(name);
    }

    /// <summary>
    /// The name a dependency is reported under. Arrays, pointers and by-refs collapse to their element type
    /// (the dependency is the type, not the shape), a generic type reports its definition, and a
    /// compiler-generated type reports the authored type it belongs to.
    /// </summary>
    private static string? DependencyName(TypeReference type)
    {
        var current = type;
        while (true)
        {
            if (current is GenericInstanceType closed) current = closed.ElementType;
            else if (current is TypeSpecification specification) current = specification.ElementType;
            else break;
        }

        // Climb out of closures and state machines to the type that was actually written by hand.
        while (current.Name.Contains('<', StringComparison.Ordinal) && current.DeclaringType is { } declaring)
            current = declaring;

        // A compiler-generated type that belongs to no authored type (the module's <PrivateImplementationDetails>,
        // anonymous types) is nobody's dependency: reporting it would only add noise no reviewer can act on.
        if (current.Name.Contains('<', StringComparison.Ordinal)) return null;

        return FullName(current);
    }

    /// <summary>Reflection-compatible full name: dot-separated namespace, nested types joined with <c>+</c>.</summary>
    private static string FullName(TypeReference type)
    {
        var current = type;
        while (true)
        {
            if (current is GenericInstanceType closed) current = closed.ElementType;
            else if (current is TypeSpecification specification) current = specification.ElementType;
            else break;
        }

        return current.DeclaringType is { } declaring
            ? $"{FullName(declaring)}+{current.Name}"
            : string.IsNullOrEmpty(current.Namespace) ? current.Name : $"{current.Namespace}.{current.Name}";
    }

    private static string NamespaceOf(TypeDefinition type)
    {
        var outermost = type;
        while (outermost.DeclaringType is { } declaring) outermost = declaring;
        return outermost.Namespace;
    }
}
