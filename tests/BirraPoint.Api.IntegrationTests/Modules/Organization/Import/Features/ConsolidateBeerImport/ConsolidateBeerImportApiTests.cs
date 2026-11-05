using System.Net;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Import.Features.ConsolidateBeerImport;

/// <summary>
/// HTTP-level contract tests for POST /competitions/{id}/imports/beers/{importId}/consolidate — gating on
/// unresolved rows, BeerEntry/Participant population and re-import semantics.
/// </summary>
public sealed class ConsolidateBeerImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ConsolidateIsBlockedWith409WhileARowIsCategoryStyleMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId, styleCodes: [StyleCodeHazyIpa]);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(style: "20C. Imperial Stout"));

        var response = await ConsolidateBeerAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ConsolidateBeerAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConsolidateIsBlockedWith409WhileAnyRowIsUnresolved()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId,
            BeerRow(email: "ana@brew.example", name: "Ana Gomez"),
            BeerRow(email: "sam@brew.example", name: "Sam Roe", style: $"{UnknownStyleCode}. X"));

        var response = await ConsolidateBeerAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateIsBlockedWith409WhileARowIsCategoryMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(category: "Estilos experimentales"));

        var response = await ConsolidateBeerAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateSuccessPopulatesEveryNewBeerEntryFieldAndSetsTheCategory()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow());

        var response = await ConsolidateBeerAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("imported").GetInt32());
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        var entryId = entries[0].GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.BeerEntries.SingleAsync(e => e.Id == entryId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(categoryId, entry.CompetitionCategoryId);
        Assert.Equal(StyleCodeHazyIpa, entry.StyleCode);
        Assert.Equal(7.6m, entry.AbvPercent);
        Assert.Equal(new DateOnly(2025, 8, 12), entry.BrewDate);
        Assert.Equal(new DateOnly(2025, 8, 28), entry.BottlingDate);
        Assert.Equal("Pale Ale, Trigo, Copos de avena, Melanoidin", entry.Malts);
        Assert.Equal("Amarillo, Citra, Mosaic", entry.Hops);
        Assert.Equal("White Lab WL-001-P California Ale", entry.Yeast);
        Assert.Null(entry.BeerName);

        var participant = await db.Participants.SingleAsync(p => p.Id == entry.ParticipantId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("1423", participant.AcceMemberNumber);
        Assert.Equal("699989612", participant.Phone);
    }

    [Fact]
    public async Task ConsolidateASecondTimeOnTheSameBatchIsRejectedWith409AndDoesNotDuplicateEntries()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow());

        var first = await ConsolidateBeerAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await ConsolidateBeerAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        await SetStandardCategoryAsync(owner, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(owner, competitionId, BeerRow());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await ConsolidateBeerAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConsolidateReimportingTheSameEmailWithinOneCompetitionUpdatesTheExistingParticipant()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (firstImportId, _) = await UploadBeerRowsAsync(organizer, competitionId,
            BeerRow(email: "dezaprieto@gmail.com", name: "Jose Deza", acceMemberNumber: 1000.0));
        await ConsolidateBeerAsync(organizer, competitionId, firstImportId);

        var (secondImportId, _) = await UploadBeerRowsAsync(organizer, competitionId,
            BeerRow(email: "dezaprieto@gmail.com", name: "José Deza Prieto (updated)", acceMemberNumber: 2000.0,
                style: "20C. Imperial Stout"));
        var consolidate = await ConsolidateBeerAsync(organizer, competitionId, secondImportId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var participants = await db.Participants
            .Where(p => p.CompetitionId == competitionId && p.Email == "dezaprieto@gmail.com")
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(participants);
        Assert.Equal("José Deza Prieto (updated)", participants[0].Name);
        Assert.Equal("2000", participants[0].AcceMemberNumber);

        var entryCount = await db.BeerEntries.CountAsync(e => e.ParticipantId == participants[0].Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, entryCount);
    }

    [Fact]
    public async Task ConsolidateTheSameParticipantEmailInADifferentCompetitionCreatesAnIndependentParticipantRow()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var firstCompetitionId = await CreateCompetitionAsync(organizer, "ImportFirst");
        await SetStandardCategoryAsync(organizer, firstCompetitionId);
        var (firstImportId, _) = await UploadBeerRowsAsync(organizer, firstCompetitionId,
            BeerRow(email: "shared@brew.example", name: "Name In First Competition"));
        await ConsolidateBeerAsync(organizer, firstCompetitionId, firstImportId);

        var secondCompetitionId = await CreateCompetitionAsync(organizer, "ImportSecond");
        await SetStandardCategoryAsync(organizer, secondCompetitionId);
        var (secondImportId, _) = await UploadBeerRowsAsync(organizer, secondCompetitionId,
            BeerRow(email: "shared@brew.example", name: "Name In Second Competition"));
        await ConsolidateBeerAsync(organizer, secondCompetitionId, secondImportId);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstParticipant = await db.Participants
            .SingleAsync(p => p.CompetitionId == firstCompetitionId && p.Email == "shared@brew.example", cancellationToken: TestContext.Current.CancellationToken);
        var secondParticipant = await db.Participants
            .SingleAsync(p => p.CompetitionId == secondCompetitionId && p.Email == "shared@brew.example", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(firstParticipant.Id, secondParticipant.Id);
        Assert.Equal("Name In First Competition", firstParticipant.Name);
        Assert.Equal("Name In Second Competition", secondParticipant.Name);
    }
}
