using Xunit;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 scenarios are serialised against each other and against the rest of the suite.
///
/// Reason: every test class here gets its own throwaway database from <see cref="PostgresFixture"/>, which means a
/// <c>CREATE DATABASE ... </c> against <c>template1</c> plus a full Init.sql + migration replay per class. Those are
/// the only server-wide, cross-class couplings these tests have (row data cannot leak: each class owns its database).
/// Keeping the six scenarios in one non-parallel collection keeps that burst of DDL from landing on PostgreSQL at the
/// same moment as the other ~110 integration classes, so a Phase 0 result can only be about the rental lifecycle and
/// never about fixture setup losing a race.
///
/// Each class keeps its own <see cref="PostgresFixture"/> (own database), its own rows and its own ids: the facts stay
/// independent and order-free, they just do not run at the same instant as everything else.
/// </summary>
[CollectionDefinition("Phase0", DisableParallelization = true)]
public sealed class Phase0Collection
{
    /// <summary>Must match the name used in <see cref="CollectionDefinitionAttribute"/> above.</summary>
    public const string Name = "Phase0";
}
