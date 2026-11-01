using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Tables.Features.ListTables;

/// <summary>GET /competitions/{id}/tables: judges, samples, state and stats (meanAbv, styles, categories).</summary>
public sealed class ListTablesApiTests(ApiFactory factory) : TablesApiTestBase(factory), IClassFixture<ApiFactory>
{
    // ---- GET /tables ----------------------------------------------------------------------------

    [Fact]
    public async Task GetWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetTablesAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetTablesAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnsTablesWithJudgesSamplesAndState()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");
        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");
        var tableName = $"Table {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, tableName, [judgeId], [entryId]);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var tables = document.RootElement.EnumerateArray().ToList();
        Assert.Single(tables);
        var table = tables[0];
        Assert.Equal(tableName, table.GetProperty("name").GetString());
        Assert.Equal("Open", table.GetProperty("state").GetString());
        Assert.Contains(judgeId, ExtractIds(table.GetProperty("judges"), "judgeId", "id"));
        Assert.Contains(entryId, ExtractIds(table.GetProperty("samples"), "beerEntryId", "id"));
    }

    // ---- GET /tables: stats.meanAbv -----------------------------------------------------

    [Fact]
    public async Task GetReturnsTheEntrysActualAbvPercentOnEachSample()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon", abvPercent: 6.50m);
        var tableName = $"Table {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, tableName, [], [entryId]);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = document.RootElement.EnumerateArray().Single();
        var sample = table.GetProperty("samples").EnumerateArray().Single(s => s.GetProperty("beerEntryId").GetGuid() == entryId);
        Assert.Equal(6.50m, sample.GetProperty("abvPercent").GetDecimal());
    }

    [Fact]
    public async Task GetComputesMeanAbvFromTheEntriesActualAbvNotTheBjcpStyleRange()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        // Both entries share StyleCodeApa (21A, American IPA — a real declared ABV range in the
        // catalog), but their real submitted ABV differs from the style midpoint and from each
        // other — proves meanAbv is the average of actual AbvPercent, not the style's range.
        var entry1 = await SeedBeerEntryAsync(competitionId, participantId, "Brew 1", abvPercent: 5.00m);
        var entry2 = await SeedBeerEntryAsync(competitionId, participantId, "Brew 2", abvPercent: 7.00m);
        var tableName = $"Table {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, tableName, [], [entry1, entry2]);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = document.RootElement.EnumerateArray().Single();
        Assert.Equal(6.00m, table.GetProperty("stats").GetProperty("meanAbv").GetDecimal());
    }

    [Fact]
    public async Task GetComputesMeanAbvFromRealAbvEvenWhenTheStylesBjcpRangeIsUndeclared()
    {
        // Regression for the previous bug: MeanAbv used to average the BJCP style's declared
        // ABVLow/ABVHigh midpoint, so a style with no declared range (real BJCP catalog styles
        // like "27-Kellerbier" legitimately have none) was excluded from the average entirely —
        // a single-entry table on such a style produced a null MeanAbv even though the
        // organizer had the entry's real submitted ABV all along.
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(
            competitionId, participantId, "Kellerbier Brew", styleCode: "27-Kellerbier", abvPercent: 4.80m);
        var tableName = $"Table {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, tableName, [], [entryId]);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = document.RootElement.EnumerateArray().Single();
        var sample = table.GetProperty("samples").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, sample.GetProperty("abvLow").ValueKind);
        Assert.Equal(JsonValueKind.Null, sample.GetProperty("abvHigh").ValueKind);
        Assert.Equal(4.80m, table.GetProperty("stats").GetProperty("meanAbv").GetDecimal());
    }

    [Fact]
    public async Task GetReturnsANullMeanAbvForATableWithZeroSamples()
    {
        // rest-api.md now says explicitly that meanAbv is "null only when the table has zero
        // samples" — closing the coverage gap flagged in PR #31 review finding #8 (all three
        // tests above seed at least one entry).
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableName = $"Table {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, tableName, [], []);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = document.RootElement.EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, table.GetProperty("stats").GetProperty("meanAbv").ValueKind);
    }

    // ---- GET /tables: stats.styleCount/styles (PR #31 review finding #7) ----------------------

    [Fact]
    public async Task GetStillCountsASampleWhoseStyleCodeHasNoMatchingBjcpRowInStats()
    {
        // Same "silently excluded" defect shape as the previous MeanAbv bug, spotted in PR #31
        // review finding #7: StyleCount/Styles used to be derived from the catalog-joined
        // `styles` rows rather than from `samples` (which already falls back to the entry's
        // raw StyleCode via `style?.Name ?? e.StyleCode`) — an entry whose StyleCode has no
        // matching BjcpStyles row was silently dropped from the style stats. The
        // BeerEntries.StyleCode FK to BjcpStyles prevents creating such a row through the
        // normal insert path, so SeedBeerEntryWithUnmatchedStyleCodeAsync bypasses FK
        // enforcement for one transaction to prove the projector degrades gracefully for the
        // scenario the code's own `style?.Name ?? e.StyleCode` fallback already anticipates.
        const string orphanStyleCode = "ZZ-NO-CATALOG-ROW";
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryWithUnmatchedStyleCodeAsync(competitionId, participantId, orphanStyleCode);
        var tableName = $"Table {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, tableName, [], [entryId]);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = document.RootElement.EnumerateArray().Single();
        var stats = table.GetProperty("stats");
        Assert.Equal(1, stats.GetProperty("styleCount").GetInt32());
        Assert.Contains(orphanStyleCode, stats.GetProperty("styles").EnumerateArray().Select(s => s.GetString()));
    }

    /// <summary>Inserts a BeerEntry whose StyleCode has no matching BjcpStyles row — bypasses the
    /// DB-enforced FK (`FK_BeerEntries_BjcpStyles_StyleCode`) for the single insert statement via
    /// `SET LOCAL session_replication_role = replica`, scoped to one transaction so it can't leak
    /// into other tests sharing this class's Testcontainer.</summary>
    private async Task<Guid> SeedBeerEntryWithUnmatchedStyleCodeAsync(Guid competitionId, Guid participantId, string styleCode)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica;");
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = "Orphan Style Brew",
            StyleCode = styleCode,
            BlindCode = NewBlindCode(),
            AbvPercent = 5.00m,
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return entry.Id;
    }

    // ---- GET /tables: competition category + BJCP taxonomy category per sample ----------

    [Fact]
    public async Task GetReturnsTheCompetitionCategoryAndBjcpCategoryOnEachSample()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var categoryId = await SeedCompetitionCategoryAsync(competitionId, "Estilos clasicos");
        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(
            competitionId, participantId, "Hop Cannon", competitionCategoryId: categoryId);
        await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [], [entryId]);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = document.RootElement.EnumerateArray().Single();
        var sample = table.GetProperty("samples").EnumerateArray()
            .Single(s => s.GetProperty("beerEntryId").GetGuid() == entryId);

        Assert.Equal("Estilos clasicos", sample.GetProperty("competitionCategoryName").GetString());

        var (categoryNumber, categoryName) = await CatalogCategoryOfAsync(StyleCodeApa);
        Assert.Equal(categoryNumber, sample.GetProperty("bjcpCategoryNumber").GetString());
        Assert.Equal(categoryName, sample.GetProperty("bjcpCategoryName").GetString());
    }

    [Fact]
    public async Task GetReturnsANullCompetitionCategoryNameOnASampleWithoutOne()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");
        await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [], [entryId]);

        var response = await GetTablesAsync(organizer, competitionId);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var sample = document.RootElement.EnumerateArray().Single()
            .GetProperty("samples").EnumerateArray().Single();

        Assert.Equal(JsonValueKind.Null, sample.GetProperty("competitionCategoryName").ValueKind);
        Assert.False(string.IsNullOrEmpty(sample.GetProperty("bjcpCategoryName").GetString()));
    }
}
