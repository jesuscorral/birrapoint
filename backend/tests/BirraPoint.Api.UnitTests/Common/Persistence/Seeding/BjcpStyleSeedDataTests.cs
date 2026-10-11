using BirraPoint.Api.Common.Persistence.Seeding;

namespace BirraPoint.Api.UnitTests.Common.Persistence.Seeding;

/// <summary>
/// DB-free guard on the embedded BJCP 2021 catalog resource itself (T010): catches
/// transcription/shape problems before they ever reach the seed migration.
/// </summary>
public sealed class BjcpStyleSeedDataTests
{
    [Fact]
    public void CatalogHasExactly125EntriesCoveringCategories1Through34PlusAppendixB()
    {
        var styles = BjcpStyleCatalogLoader.Load();

        Assert.Equal(125, styles.Count);
    }

    [Fact]
    public void EveryCodeIsUniqueAndWithinThe20CharacterColumnWidth()
    {
        var styles = BjcpStyleCatalogLoader.Load();

        var codes = styles.Select(s => s.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, code => Assert.True(code.Length <= 20, $"'{code}' exceeds 20 chars"));
    }

    [Fact]
    public void EveryEntryCarriesACompleteDescription()
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
    public void AmericanIpaVitalStatisticsArePresent()
    {
        var styles = BjcpStyleCatalogLoader.Load();

        var americanIpa = Assert.Single(styles, s => s.Code == "21A");
        Assert.Equal("American IPA", americanIpa.Name);
        Assert.Equal("21", americanIpa.CategoryNumber);
        Assert.NotNull(americanIpa.VitalStatistics.OgLow);
        Assert.NotNull(americanIpa.VitalStatistics.AbvHigh);
    }

    /// <summary>
    /// Pins the embedded resource's content hash to the value AddBjcpStyleCatalogDetails seeded
    /// its 125 rows from. If this fails, bjcp-2021.json was edited after that migration shipped:
    /// already-migrated databases will NOT pick up the change automatically. Either revert the
    /// edit, or ship a new follow-up migration that updates the existing rows and update this
    /// pinned hash in the same change.
    /// </summary>
    [Fact]
    public void EmbeddedCatalogContentHashMatchesTheValueSeededByAddBjcpStyleCatalogDetails()
    {
        var hash = BjcpStyleCatalogLoader.ComputeContentHash();

        Assert.Equal("70BD66B2617804316DE7BCDCFEBEB87727E0392370B0ABDE22F2C769E35E5208", hash);
    }
}
