using System.Reflection;
using MaintTrack.Application.Maintenance;
using MaintTrack.Infrastructure.Maintenance;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// Submitted maintenance reports are immutable, so the maintenance service must not offer any
/// operation that changes an existing report. Adding one has to be a deliberate change to this test.
/// </summary>
public sealed class MaintenanceEntryImmutabilityContractTests
{
    private static readonly string[] AllowedOperations = { "CreateAsync", "DeleteAsync", "GetAsync", "GetByIdAsync" };

    [Fact]
    public void ServiceInterface_ExposesNoUpdateOperation()
    {
        var methods = typeof(IMaintenanceEntryService).GetMethods().Select(m => m.Name).OrderBy(n => n);

        Assert.Equal(AllowedOperations.OrderBy(n => n), methods);
    }

    [Fact]
    public void ServiceImplementation_HasNoPublicWriteBackdoor()
    {
        var methods = typeof(MaintenanceEntryService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n);

        Assert.Equal(AllowedOperations.OrderBy(n => n), methods);
    }
}
