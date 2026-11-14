using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Pins the user-visible <c>message</c> every controller action puts in the success envelope, so moving the
/// wording around (a controller split, a rename, an attribute instead of a lookup table) can never silently
/// change what clients read. The golden file records one line per action, HTTP verb, success status code and
/// the post-rental query variant of the inspection-assignment endpoints.
/// </summary>
/// <remarks>
/// <para>
/// The messages are produced by the real <see cref="ApiSuccessResponseFilter"/> running over the real action
/// descriptors the host publishes, taken from the <see cref="CompositionSmokeTests.RealProgramFactory"/> host,
/// so this test inherits that factory's safety rails — no database, no Redis, no hosted service — and never
/// boots Program.cs against the real appsettings.json.
/// </para>
/// <para>
/// Regenerating is deliberate and can never pass silently: set <c>UPDATE_SUCCESS_MESSAGES_SNAPSHOT=1</c>, run
/// the test, and the golden file is rewritten while the test fails, so the regenerated lines must be reviewed
/// and committed with the change that caused them.
/// </para>
/// </remarks>
public sealed class SuccessMessageContractTests : IClassFixture<CompositionSmokeTests.RealProgramFactory>
{
    private const string SnapshotVariable = "UPDATE_SUCCESS_MESSAGES_SNAPSHOT";
    private const string SnapshotFile = "Contracts/success-messages.v1.txt";

    /// <summary>The one query string the success wording is allowed to vary on.</summary>
    private const string PostRentalQuery = "inspectionType=post_rental";

    /// <summary>The success status codes whose wording differs from the 200 default.</summary>
    private static readonly int[] WordingStatusCodes =
    [
        StatusCodes.Status200OK,
        StatusCodes.Status201Created,
        StatusCodes.Status202Accepted,
        StatusCodes.Status204NoContent
    ];

    private readonly CompositionSmokeTests.RealProgramFactory _factory;

    public SuccessMessageContractTests(CompositionSmokeTests.RealProgramFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_action_reports_the_committed_success_message()
    {
        var actual = await RenderAsync();

        var snapshot = SnapshotPath();
        if (Environment.GetEnvironmentVariable(SnapshotVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            await File.WriteAllBytesAsync(snapshot, actual);
            Assert.Fail($"{SnapshotVariable}=1: rewrote {SnapshotFile} with {actual.Length} bytes (SHA-256 {Hash(actual)}). " +
                        $"Re-run without {SnapshotVariable} and review the diff before committing it.");
        }

        Assert.True(File.Exists(snapshot),
            $"The committed snapshot {SnapshotFile} is missing. Create it deliberately with {SnapshotVariable}=1.");
        var expected = await File.ReadAllBytesAsync(snapshot);

        Assert.True(expected.AsSpan().SequenceEqual(actual), MismatchMessage(expected, actual));
    }

    /// <summary>
    /// Runs every action/verb/status/query combination through the real filter and renders the resulting
    /// messages as sorted <c>route | controller.action | VERB | status | query | message</c> lines.
    /// </summary>
    private async Task<byte[]> RenderAsync()
    {
        var descriptors = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToList();
        Assert.NotEmpty(descriptors);

        var lines = new List<string>();
        foreach (var descriptor in descriptors)
            foreach (var verb in Verbs(descriptor))
                foreach (var statusCode in SuccessStatusCodes(descriptor))
                    foreach (var query in new[] { (string?)null, PostRentalQuery })
                        lines.Add(await DescribeAsync(descriptor, verb, statusCode, query));

        lines.Sort(StringComparer.Ordinal);
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.Ordinal).Count());

        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    /// <summary>
    /// Feeds one result through <see cref="ApiSuccessResponseFilter"/> and reads the message the envelope
    /// carries, exactly as a client would see it.
    /// </summary>
    private async Task<string> DescribeAsync(ControllerActionDescriptor descriptor, string verb, int statusCode, string? query)
    {
        var httpContext = new DefaultHttpContext { RequestServices = _factory.Services };
        httpContext.Request.Method = verb;
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        if (query is not null)
            httpContext.Request.QueryString = new QueryString("?" + query);

        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor, new ModelStateDictionary());
        var context = new ResultExecutingContext(actionContext, Array.Empty<IFilterMetadata>(), ResultFor(statusCode), controller: null!);
        await new ApiSuccessResponseFilter().OnResultExecutionAsync(context, () => Task.FromResult<ResultExecutedContext>(null!));

        var envelope = Assert.IsAssignableFrom<ApiResponse>(Assert.IsAssignableFrom<ObjectResult>(context.Result).Value);
        return string.Join(" | ",
            "/" + (descriptor.AttributeRouteInfo?.Template ?? "<no route>"),
            $"{descriptor.ControllerName}.{descriptor.ActionName}",
            verb,
            statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            query ?? "-",
            envelope.Message);
    }

    private static IActionResult ResultFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status200OK => new OkObjectResult(new { sample = true }),
        StatusCodes.Status204NoContent => new NoContentResult(),
        _ => new ObjectResult(new { sample = true }) { StatusCode = statusCode }
    };

    /// <summary>
    /// The verbs the action accepts, read from the routing constraints the framework itself uses, so a verb
    /// change is a visible difference instead of an assumption.
    /// </summary>
    private static IReadOnlyList<string> Verbs(ControllerActionDescriptor descriptor)
    {
        var verbs = descriptor.ActionConstraints?.OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList() ?? [];
        Assert.True(verbs.Count > 0, $"{descriptor.DisplayName} declares no HTTP method constraint.");
        return verbs;
    }

    /// <summary>
    /// The declared success status codes whose wording can differ, always including 200 because the filter
    /// falls back to it whenever a result carries no status code of its own.
    /// </summary>
    private static IReadOnlyList<int> SuccessStatusCodes(ControllerActionDescriptor descriptor) =>
        descriptor.MethodInfo.GetCustomAttributes(true)
            .OfType<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Where(WordingStatusCodes.Contains)
            .Append(StatusCodes.Status200OK)
            .Distinct()
            .Order()
            .ToList();

    /// <summary>
    /// The golden file lives next to this test in the source tree, not in the output folder, so a
    /// regeneration edits the file that gets committed.
    /// </summary>
    private static string SnapshotPath([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, SnapshotFile.Replace('/', Path.DirectorySeparatorChar));

    private static string MismatchMessage(byte[] expected, byte[] actual) => string.Join(Environment.NewLine,
    [
        $"The success messages no longer match the committed snapshot {SnapshotFile}.",
        $"Snapshot: {expected.Length} bytes, SHA-256 {Hash(expected)}",
        $"Produced: {actual.Length} bytes, SHA-256 {Hash(actual)}",
        $"First difference: {FirstDifference(expected, actual)}",
        $"If the change is intended, regenerate the snapshot with {SnapshotVariable}=1, review the diff, and commit it " +
        "together with the change that caused it."
    ]);

    /// <summary>Names the first differing line and shows both sides of it.</summary>
    private static string FirstDifference(byte[] expected, byte[] actual)
    {
        var expectedLines = Encoding.UTF8.GetString(expected).Replace("\r\n", "\n").Split('\n');
        var actualLines = Encoding.UTF8.GetString(actual).Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < Math.Min(expectedLines.Length, actualLines.Length); index++)
            if (expectedLines[index] != actualLines[index])
                return $"line {index + 1}{Environment.NewLine}  snapshot: {expectedLines[index]}{Environment.NewLine}  produced: {actualLines[index]}";

        return $"line {Math.Min(expectedLines.Length, actualLines.Length) + 1} — the snapshot has " +
               $"{expectedLines.Length} lines and the run produced {actualLines.Length}";
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
