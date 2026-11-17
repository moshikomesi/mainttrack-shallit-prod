using MaintTrack.Domain.Translations;

namespace MaintTrack.Tests.Translations;

public sealed class MaintenanceDescriptionTextTests
{
    private const string Narrative = "Seal leaking. Replaced the gasket.";

    [Theory]
    [InlineData("maintenanceComponent.bearing")]
    [InlineData("maintenanceComponent.other")]
    [InlineData("Hydraulic pump")]
    [InlineData("Other: Hydraulic pump")]
    public void MarkedV2_ExcludesEveryComponentLineFromTheNarrative(string component)
    {
        var description = $"maintenanceLog.v2\n{component}\n{Narrative}";

        var parts = MaintenanceDescriptionText.Extract(description);

        Assert.Equal(MaintenanceDescriptionVersion.V2, parts.Version);
        Assert.Equal(component, parts.Component);
        Assert.Equal(Narrative, parts.Narrative);
        Assert.DoesNotContain(component, parts.Narrative, StringComparison.Ordinal);
        Assert.True(parts.HasTranslatableNarrative);
    }

    [Fact]
    public void MarkedV2_WithComponentAndNoNarrative_IsNotTranslatable()
    {
        var parts = MaintenanceDescriptionText.Extract("maintenanceLog.v2\nOther: Hydraulic pump");

        Assert.Equal(MaintenanceDescriptionVersion.V2, parts.Version);
        Assert.Equal("Other: Hydraulic pump", parts.Component);
        Assert.Equal(string.Empty, parts.Narrative);
        Assert.False(parts.HasTranslatableNarrative);
    }

    [Fact]
    public void MarkedV1_TranslatesEveryLineAfterTheSentinel()
    {
        const string description = "maintenanceLog.v1\nHydraulic pump\nSeal leaking.";

        var parts = MaintenanceDescriptionText.Extract(description);

        Assert.Equal(MaintenanceDescriptionVersion.V1, parts.Version);
        Assert.Null(parts.Component);
        Assert.Equal("Hydraulic pump\nSeal leaking.", parts.Narrative);
        Assert.True(parts.HasTranslatableNarrative);
    }

    [Fact]
    public void UnmarkedV1ShapedDescription_IsNotTranslatable()
    {
        var parts = MaintenanceDescriptionText.Extract("Seal leaking. Replaced the gasket.");

        Assert.Equal(MaintenanceDescriptionVersion.Unmarked, parts.Version);
        Assert.Equal(string.Empty, parts.Narrative);
        Assert.False(parts.HasTranslatableNarrative);
    }

    [Fact]
    public void UnmarkedV2ShapedDescription_IsNotTranslatable()
    {
        var parts = MaintenanceDescriptionText.Extract("Hydraulic pump\nSeal leaking. Replaced the gasket.");

        Assert.Equal(MaintenanceDescriptionVersion.Unmarked, parts.Version);
        Assert.Null(parts.Component);
        Assert.Equal(string.Empty, parts.Narrative);
        Assert.False(parts.HasTranslatableNarrative);
    }
}
