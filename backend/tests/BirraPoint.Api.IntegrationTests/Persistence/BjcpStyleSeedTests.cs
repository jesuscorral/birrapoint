using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.IntegrationTests.Persistence;

/// <summary>
/// T010: the full BJCP 2021 catalog (125 entries, categories 1–34 + Appendix B) must be
/// present after the seed migration runs, with valid jsonb descriptions.
/// </summary>
public sealed class BjcpStyleSeedTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private AppDbContext NewContext() => new(fixture.Options);

    [Fact]
    public async Task Migration_seeds_the_full_catalog()
    {
        await using var db = NewContext();

        Assert.Equal(125, await db.BjcpStyles.CountAsync());
    }

    [Fact]
    public async Task American_ipa_row_carries_vital_statistics_and_description()
    {
        await using var db = NewContext();

        var style = await db.BjcpStyles.AsNoTracking().SingleAsync(s => s.Code == "21A");

        Assert.Equal("American IPA", style.Name);
        Assert.Equal("21", style.CategoryNumber);
        Assert.NotNull(style.OGLow);
        Assert.NotNull(style.ABVHigh);

        using var description = JsonDocument.Parse(style.DescriptionJson);
        Assert.True(description.RootElement.TryGetProperty("overallImpression", out _));
        Assert.True(description.RootElement.TryGetProperty("commercialExamples", out _));
    }

    [Fact]
    public async Task Historical_style_with_a_synthetic_slug_code_is_seeded()
    {
        await using var db = NewContext();

        var style = await db.BjcpStyles.AsNoTracking().SingleAsync(s => s.Code == "27-Sahti");

        Assert.Equal("27", style.CategoryNumber);
        Assert.NotNull(style.OGLow); // Sahti carries real vital statistics
        using var description = JsonDocument.Parse(style.DescriptionJson);
        Assert.True(description.RootElement.TryGetProperty("aroma", out _));
    }

    [Fact]
    public async Task Specialty_style_with_no_fixed_vital_statistics_is_seeded_with_null_ranges()
    {
        await using var db = NewContext();

        var style = await db.BjcpStyles.AsNoTracking().SingleAsync(s => s.Code == "28A");

        Assert.Equal("American Wild Ale", style.CategoryName);
        Assert.Null(style.OGLow);
        Assert.Null(style.ABVHigh);
    }

    [Fact]
    public async Task Every_seeded_row_has_valid_json_description()
    {
        await using var db = NewContext();

        var descriptions = await db.BjcpStyles.AsNoTracking().Select(s => s.DescriptionJson).ToListAsync();

        Assert.All(descriptions, json => JsonDocument.Parse(json).Dispose());
    }
}
