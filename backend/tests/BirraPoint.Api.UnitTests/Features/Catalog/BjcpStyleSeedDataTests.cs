using BirraPoint.Api.Features.Catalog.Data;

namespace BirraPoint.Api.UnitTests.Features.Catalog;

/// <summary>
/// DB-free guard on the embedded BJCP 2021 catalog resource itself (T010): catches
/// transcription/shape problems before they ever reach the seed migration.
/// </summary>
public sealed class BjcpStyleSeedDataTests
{
    [Fact]
    public void Catalog_has_exactly_125_entries_covering_categories_1_through_34_plus_appendix_b()
    {
        var styles = BjcpStyleCatalogLoader.Load();

        Assert.Equal(125, styles.Count);
    }

    [Fact]
    public void Every_code_is_unique_and_within_the_20_character_column_width()
    {
        var styles = BjcpStyleCatalogLoader.Load();

        var codes = styles.Select(s => s.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, code => Assert.True(code.Length <= 20, $"'{code}' exceeds 20 chars"));
    }

    [Fact]
    public void Every_entry_carries_a_complete_description()
    {
        var styles = BjcpStyleCatalogLoader.Load();

        Assert.All(styles, style =>
        {
            Assert.False(string.IsNullOrWhiteSpace(style.Description.OverallImpression));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.Aroma));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.Appearance));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.Flavor));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.Mouthfeel));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.Comments));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.History));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.CharacteristicIngredients));
            Assert.False(string.IsNullOrWhiteSpace(style.Description.StyleComparison));
            Assert.NotNull(style.Description.CommercialExamples);
            Assert.NotNull(style.Description.Tags);
        });
    }

    [Fact]
    public void American_ipa_vital_statistics_are_present()
    {
        var styles = BjcpStyleCatalogLoader.Load();

        var americanIpa = Assert.Single(styles, s => s.Code == "21A");
        Assert.Equal("American IPA", americanIpa.Name);
        Assert.Equal("21", americanIpa.CategoryNumber);
        Assert.NotNull(americanIpa.VitalStatistics.OgLow);
        Assert.NotNull(americanIpa.VitalStatistics.AbvHigh);
    }
}
