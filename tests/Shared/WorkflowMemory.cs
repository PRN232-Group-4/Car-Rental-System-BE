using Car_Rental_System_BE.Application.Interfaces.External;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// In-memory <see cref="IWorkflowStateStore"/> shared by the Application unit tests
/// (<c>AuthenticationLimitsTests</c>) and the Integration tests (<c>RefreshAuthorizationTests</c>).
/// Linked into both projects as a Compile item so neither test project has to depend on the other.
/// </summary>
/// <remarks>
/// WARNING: this file is compiled into TWO test assemblies. Never add a fact, theory or any xunit
/// attribute here: it would be discovered and executed twice by <c>dotnet test</c> on the solution.
/// Keep it a pure helper.
/// </remarks>
public sealed class WorkflowMemory : IWorkflowStateStore
{
    public Dictionary<string, string> Values { get; } = [];
    private readonly Dictionary<string, DateTimeOffset> expiries = [];
    public Task<string?> GetAsync(string key, CancellationToken ct)
    { lock (Values) { RemoveExpired(key); return Task.FromResult(Values.GetValueOrDefault(key)); } }
    public Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken ct)
    { lock (Values) { RemoveExpired(key); return Task.FromResult<TimeSpan?>(expiries.TryGetValue(key, out var expiry) ? expiry - DateTimeOffset.UtcNow : null); } }
    public Task<bool> CompareExchangeAsync(string key, string? expected, string? replacement, TimeSpan expiry, CancellationToken ct)
    {
        Assert.True(expiry > TimeSpan.Zero);
        lock (Values)
        {
            RemoveExpired(key);
            if (Values.GetValueOrDefault(key) != expected) return Task.FromResult(false);
            if (replacement is null) Expire(key);
            else { Values[key] = replacement; expiries[key] = DateTimeOffset.UtcNow.Add(expiry); }
            return Task.FromResult(true);
        }
    }
    public void Expire(string key) { lock (Values) { Values.Remove(key); expiries.Remove(key); } }
    private void RemoveExpired(string key) { if (expiries.TryGetValue(key, out var expiry) && expiry <= DateTimeOffset.UtcNow) Expire(key); }
}
