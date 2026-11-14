using Xunit;

namespace VehicleRental.Architecture.Tests;

/// <summary>
/// Locks the end of the layer-wide folders the feature refactor replaced: no type may live in
/// <c>Application.Services</c>, <c>Application.Interfaces.*</c>, <c>Application.Validators</c>,
/// <c>Domain.Entities</c>, <c>Domain.Services</c>, <c>Infrastructure.Repositories</c>,
/// <c>Infrastructure.Services</c> or <c>WebApi.Controllers</c> any more.
/// </summary>
/// <remarks>
/// <para>
/// Why a rule of its own: the layering rules describe which layer may depend on which, and the feature-privacy rule
/// describes one slice reaching into another. Neither says anything about a bucket that spans every feature of a
/// layer - <c>Application.Services</c> held thirty services from thirty modules, <c>Infrastructure.Repositories</c>
/// held the writers three slices shared. Those buckets are where the duplicate helpers came from: two copies of the
/// same read only happen when nothing owns the file. The target layout is
/// <c>&lt;Layer&gt;/Features/&lt;Module&gt;</c>, with the shared kernel in <c>Infrastructure/Persistence</c> and the
/// shared HTTP pieces in <c>WebApi/Common</c>.
/// </para>
/// <para>
/// The allowlist below is exact and fully qualified, in the style of <see cref="FeaturePrivacyTests"/>: it names the
/// leftovers that legitimately still sit outside a feature, and it may only shrink. Adding an entry is a design
/// decision, not a test fix.
/// </para>
/// </remarks>
public sealed class LegacyLayoutTests
{
    /// <summary>Folders the refactor retired. A type in one of them, or in a namespace under one, is a violation.</summary>
    private static readonly string[] RetiredNamespaces =
    [
        "Car_Rental_System_BE.Application.Services",
        "Car_Rental_System_BE.Application.Interfaces.Services",
        "Car_Rental_System_BE.Application.Interfaces.Repositories",
        "Car_Rental_System_BE.Application.Validators",
        "Car_Rental_System_BE.Domain.Entities",
        "Car_Rental_System_BE.Domain.Services",
        "Car_Rental_System_BE.Infrastructure.Repositories",
        "Car_Rental_System_BE.Infrastructure.Services",
        "Car_Rental_System_BE.WebApi.Controllers",
        "Car_Rental_System_BE.WebApi.Middlewares",
    ];

    /// <summary>
    /// The leftovers, one exact fully-qualified type name per line. May only shrink.
    /// </summary>
    private static readonly string[] Leftovers =
    [
        // The home/index endpoint has no module of its own: it answers for the host, not for a feature.
        "Car_Rental_System_BE.WebApi.Controllers.HomeController",
        // Brevo options and the OTP mail are shared by two slices - the Identity OTP sender and the Notifications
        // operational sender - so the folder belongs to neither. Moving it into one would create the exact
        // cross-feature edge the feature-privacy rule forbids.
        "Car_Rental_System_BE.Infrastructure.Services.Registration.BrevoOptions",
        "Car_Rental_System_BE.Infrastructure.Services.Registration.BrevoEmailSender",
        "Car_Rental_System_BE.Infrastructure.Services.Registration.OtpEmailTemplate",
    ];

    [Fact]
    public void Types_do_not_live_in_a_retired_layer_folder()
    {
        var violations = Violations();

        Assert.True(violations.Count == 0,
            "A type still lives in a layer-wide folder the feature refactor retired. Move it into " +
            "<Layer>/Features/<Module>, the shared kernel Infrastructure/Persistence, or WebApi/Common. Extending " +
            "the allowlist of leftovers needs a design decision (it may only shrink):" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>Guards the allowlist from outliving the leftovers it describes: it may only shrink.</summary>
    [Fact]
    public void Retired_folder_allowlist_entries_all_still_exist()
    {
        var current = Violations().Concat(Leftovers).ToHashSet(StringComparer.Ordinal);
        var declared = DeclaredTypes();

        var stale = Leftovers
            .Where(entry => !current.Contains(entry) || !declared.Contains(entry))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "These allowlist entries no longer describe a type in a retired folder. Delete them: the allowlist may " +
            "only shrink." + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    // ---------------------------------------------------------------------------------------------
    // The rule.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Every authored type of the four source assemblies, one per line.</summary>
    private static IReadOnlyList<IlType> Types() =>
    [
        .. new[] { Layers.Domain, Layers.Application, Layers.Infrastructure, Layers.WebApi }
            .SelectMany(assembly => Layers.Of(assembly).Types),
    ];

    private static HashSet<string> DeclaredTypes() =>
        Types().Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

    private static List<string> Violations() =>
    [
        .. Types()
            .Where(type => RetiredNamespaces.Any(retired => IsOrUnder(type.Namespace, retired)))
            .Where(type => !Leftovers.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => $"{type.Name} ({type.Namespace})")
            .Order(StringComparer.Ordinal),
    ];

    private static bool IsOrUnder(string @namespace, string retired) =>
        @namespace == retired || @namespace.StartsWith(retired + ".", StringComparison.Ordinal);
}
