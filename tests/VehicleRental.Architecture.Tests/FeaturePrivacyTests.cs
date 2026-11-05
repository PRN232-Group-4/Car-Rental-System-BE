using Xunit;

namespace VehicleRental.Architecture.Tests;

/// <summary>
/// A feature inside Infrastructure owns its internals: <c>Infrastructure.Features.&lt;X&gt;</c> belongs to feature
/// X, and nothing outside that namespace but the composition root may depend on it.
/// </summary>
/// <remarks>
/// <para>
/// Why a rule of its own: the layering rules say nothing about how one slice of a layer may use another. Once a
/// module lives under <c>Features/&lt;X&gt;</c>, its repositories, adapters, SQL helpers and locks are X's private
/// machinery - a second feature that calls them re-creates the coupling the move was meant to break, and does it
/// silently because both types sit in the same assembly.
/// </para>
/// <para>
/// Scope: the four source assemblies only. Features outside Infrastructure (Application, Domain, WebApi) may keep
/// depending on each other through Application contracts; that is not this rule's business. Composition roots are
/// exempt because building the object graph is exactly their job: global <c>Program</c>,
/// <c>Infrastructure.DependencyInjection</c> and the <c>WebApi.Extensions</c> registrations.
/// </para>
/// <para>
/// Dependencies come from IL (see <see cref="IlScan"/>), so a call inside a method body counts. The list below is
/// the state of the tree when the rule was introduced, not a design decision: it may only shrink.
/// </para>
/// </remarks>
public sealed class FeaturePrivacyTests
{
    /// <summary>Where features live. The first segment after it is the feature a type belongs to.</summary>
    private const string FeatureRoot = "Car_Rental_System_BE.Infrastructure.Features.";

    /// <summary>
    /// Violations that already existed when the rule was written, one per line: "dependent -&gt; feature internal".
    /// Exact and fully qualified; no wildcards. This rule is new, so this is a starting baseline, not a raised
    /// whitelist - and it may only shrink.
    /// </summary>
    private static readonly string[] BaselineAtFeaturesMove =
    [
        // ---------------------------------------------------------------------------------------
        // baseline at Features move (Task C2) - may only shrink
        // ---------------------------------------------------------------------------------------
        // Availability is the shared shelf of the move: its SQL helper, its vehicle lock and its availability
        // checks are used by three other slices, and legacy Repositories still calls into two features.
        "Car_Rental_System_BE.Infrastructure.Features.Assignments.NpgsqlAssignmentRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.NpgsqlVehicleSearchRepository",
        "Car_Rental_System_BE.Infrastructure.Features.Assignments.NpgsqlAssignmentRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.Sql",
        "Car_Rental_System_BE.Infrastructure.Features.Assignments.NpgsqlAssignmentRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.VehicleLock",
        "Car_Rental_System_BE.Infrastructure.Features.Rentals.NpgsqlRentalContractDocumentRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.Sql",
        "Car_Rental_System_BE.Infrastructure.Features.Rentals.NpgsqlRentalLifecycleRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.Sql",
        "Car_Rental_System_BE.Infrastructure.Features.Rentals.NpgsqlRentalLifecycleRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.VehicleAvailabilityChecks",
        "Car_Rental_System_BE.Infrastructure.Features.Rentals.NpgsqlRentalLifecycleRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.VehicleLock",
        "Car_Rental_System_BE.Infrastructure.Features.Rentals.RentalPaymentGateAdapter -> Car_Rental_System_BE.Infrastructure.Features.Billing.RentalRefundWriter",
        "Car_Rental_System_BE.Infrastructure.Repositories.NpgsqlOperationsRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.NpgsqlVehicleSearchRepository",
        "Car_Rental_System_BE.Infrastructure.Repositories.NpgsqlOperationsRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.VehicleAvailabilityChecks",
        "Car_Rental_System_BE.Infrastructure.Repositories.NpgsqlOperationsRepository -> Car_Rental_System_BE.Infrastructure.Features.Availability.VehicleLock",
    ];

    [Fact]
    public void Infrastructure_feature_internals_are_private_to_their_feature()
    {
        var internals = FeatureInternals();
        Assert.NotEmpty(internals);

        AssertOnlyBaseline(Violations(internals));
    }

    /// <summary>Guards the baseline from outliving the coupling it describes: it may only shrink.</summary>
    [Fact]
    public void Feature_privacy_baseline_entries_are_all_still_violations()
    {
        var current = Violations(FeatureInternals());
        var stale = BaselineAtFeaturesMove.Except(current, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0,
            "These baseline entries no longer describe a real violation. Delete them: the baseline may only shrink." +
            Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    // ---------------------------------------------------------------------------------------------
    // The rule.
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> Violations(IReadOnlyList<IlType> internals)
    {
        var dependents = new[] { Layers.Domain, Layers.Application, Layers.Infrastructure, Layers.WebApi }
            .SelectMany(assembly => Layers.Of(assembly).Types)
            .Where(type => !IsCompositionRoot(type.Name))
            .ToList();

        return
        [
            .. internals.SelectMany(target => dependents
                .Where(dependent => !IsInside(dependent.Name, FeatureOf(target.Name)!) &&
                                    dependent.Dependencies.Contains(target.Name))
                .Select(dependent => $"{dependent.Name} -> {target.Name}")),
        ];
    }

    private static void AssertOnlyBaseline(IEnumerable<string> violations)
    {
        var unexpected = violations.Distinct(StringComparer.Ordinal).Except(BaselineAtFeaturesMove, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToList();

        Assert.True(unexpected.Count == 0,
            "A type outside its feature depends on a feature internal. Go through an Application contract instead " +
            "of extending the baseline (it may only shrink):" + Environment.NewLine +
            string.Join(Environment.NewLine, unexpected));
    }

    /// <summary>Every type of the Infrastructure assembly that lives inside a feature.</summary>
    private static IReadOnlyList<IlType> FeatureInternals() =>
        Layers.Of(Layers.Infrastructure).Types.Where(type => FeatureOf(type.Name) is not null).ToList();

    /// <summary>The feature a fully-qualified name belongs to: the first segment after <c>Features</c>.</summary>
    private static string? FeatureOf(string typeName) =>
        typeName.StartsWith(FeatureRoot, StringComparison.Ordinal)
            ? typeName[FeatureRoot.Length..].Split('.')[0]
            : null;

    /// <summary>True for a type inside <c>Features.&lt;feature&gt;</c>, its sub-namespaces and its nested types.</summary>
    private static bool IsInside(string typeName, string feature)
    {
        var prefix = FeatureRoot + feature;
        return typeName.StartsWith(prefix, StringComparison.Ordinal) &&
               (typeName.Length == prefix.Length || typeName[prefix.Length] is '.' or '+');
    }

    /// <summary>
    /// The composition root builds the graph, so it is the one place allowed to name a feature internal: global
    /// <c>Program</c>, <c>Infrastructure.DependencyInjection</c> and the <c>WebApi.Extensions</c> registrations.
    /// </summary>
    private static bool IsCompositionRoot(string typeName) =>
        typeName == "Program" ||
        IsOrUnder(typeName, "Car_Rental_System_BE.Infrastructure.DependencyInjection") ||
        IsOrUnder(typeName, "Car_Rental_System_BE.WebApi.Extensions");

    private static bool IsOrUnder(string typeName, string @namespace) =>
        typeName == @namespace ||
        typeName.StartsWith(@namespace + ".", StringComparison.Ordinal) ||
        typeName.StartsWith(@namespace + "+", StringComparison.Ordinal);
}
