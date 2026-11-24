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
    public void LegacyNarrativeOnly_UsesTheWholeNormalizedDescription()
    {
        const string description = "  Seal leaking.\r\nReplaced the gasket.  ";

        var parts = MaintenanceDescriptionText.Extract(description);

        Assert.Equal(MaintenanceDescriptionVersion.Legacy, parts.Version);
        Assert.Null(parts.Component);
        Assert.Equal("Seal leaking.\nReplaced the gasket.", parts.Narrative);
        Assert.True(parts.HasTranslatableNarrative);
        Assert.Equal(SourceTextHasher.Compute(parts.Narrative), SourceTextHasher.Compute("Seal leaking.\nReplaced the gasket."));
    }

    [Fact]
    public void LegacyComponentAndNarrative_KeepsTheKeyOutOfTheNarrativeHash()
    {
        var description = $"maintenanceComponent.driveShaft\r\n{Narrative}";
        var normalized = description.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

        var parts = MaintenanceDescriptionText.Extract(description);

        Assert.Equal(MaintenanceDescriptionVersion.Legacy, parts.Version);
        Assert.Equal("maintenanceComponent.driveShaft", parts.Component);
        Assert.Equal(Narrative, parts.Narrative);
        Assert.DoesNotContain("maintenanceComponent.", parts.Narrative, StringComparison.Ordinal);
        Assert.True(parts.HasTranslatableNarrative);
        Assert.Equal(SourceTextHasher.Compute(Narrative), SourceTextHasher.Compute(parts.Narrative));
        Assert.NotEqual(SourceTextHasher.Compute(normalized), SourceTextHasher.Compute(parts.Narrative));
    }

    [Theory]
    [InlineData("maintenanceComponent.driveShaft")]
    [InlineData("maintenanceComponent.bearing\n")]
    [InlineData("  maintenanceComponent.other  \r\n   ")]
    public void LegacyComponentOnly_IsNotTranslatable(string description)
    {
        var parts = MaintenanceDescriptionText.Extract(description);

        Assert.Equal(MaintenanceDescriptionVersion.Legacy, parts.Version);
        Assert.StartsWith("maintenanceComponent.", parts.Component, StringComparison.Ordinal);
        Assert.Equal(string.Empty, parts.Narrative);
        Assert.False(parts.HasTranslatableNarrative);
    }

    [Fact]
    public void LegacyFreeText_DoesNotTreatTheFirstLineAsAComponent()
    {
        var parts = MaintenanceDescriptionText.Extract($"Hydraulic pump\n{Narrative}");

        Assert.Equal(MaintenanceDescriptionVersion.Legacy, parts.Version);
        Assert.Null(parts.Component);
        Assert.Equal($"Hydraulic pump\n{Narrative}", parts.Narrative);
        Assert.True(parts.HasTranslatableNarrative);
    }

    [Fact]
    public void BlankDescription_StaysUnmarked()
    {
        var parts = MaintenanceDescriptionText.Extract("  \r\n  ");

        Assert.Equal(MaintenanceDescriptionVersion.Unmarked, parts.Version);
        Assert.Null(parts.Component);
        Assert.Equal(string.Empty, parts.Narrative);
        Assert.False(parts.HasTranslatableNarrative);
    }
}
