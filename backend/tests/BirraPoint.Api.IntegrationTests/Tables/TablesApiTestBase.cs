using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Tables;

/// <summary>Shared clients, HTTP helpers and seeding for the Tables slice contract tests
/// (contracts/rest-api.md §Tables): CreateTable, UpdateTable and ListTables test classes.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class TablesApiTestBase(ApiFactory factory)
{
    private protected ApiFactory Factory => factory;

    private protected const string StyleCodeApa = "21A";

    private protected HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private protected HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "JUDGE"));
        return client;
    }

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Tables")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"{namePrefix} {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private protected static Task<HttpResponseMessage> CreateTableAsync(
        HttpClient client, Guid competitionId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/tables", new { name, judgeIds, beerEntryIds });

    private protected static Task<HttpResponseMessage> UpdateTableAsync(
        HttpClient client, Guid competitionId, Guid tableId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/tables/{tableId}", new { name, judgeIds, beerEntryIds });

    private protected static Task<HttpResponseMessage> GetTablesAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/tables"));

    /// <summary>Extracts a set of ids from a JSON array whose elements are either raw GUID
    /// strings or objects carrying one of <paramref name="idPropertyNames"/> — the GET /tables
    /// item shape for judges/samples isn't pinned exactly by the contract doc, so this tolerates
    /// either representation while still asserting real identity, not just counts.</summary>
    private protected static HashSet<Guid> ExtractIds(JsonElement array, params string[] idPropertyNames)
    {
        var ids = new HashSet<Guid>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                ids.Add(element.GetGuid());
                continue;
            }

            foreach (var name in idPropertyNames)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    ids.Add(prop.GetGuid());
                    break;
                }
            }
        }

        return ids;
    }

    private protected static string NewBlindCode() => $"B{Guid.NewGuid():N}"[..8];

    private protected async Task<Guid> SeedParticipantAsync(Guid competitionId, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();
        return participant.Id;
    }

    private protected async Task<Guid> SeedCompetitionCategoryAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new CompetitionCategory { CompetitionId = competitionId, Name = name, DisplayOrder = 0 };
        db.CompetitionCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>Reads the seeded BJCP catalog row for <paramref name="styleCode"/> so the
    /// assertions below compare against the real catalog instead of hardcoding taxonomy values
    /// that the BJCP 2021 seed migration owns.</summary>
    private protected async Task<(string CategoryNumber, string CategoryName)> CatalogCategoryOfAsync(string styleCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var style = await db.BjcpStyles.SingleAsync(s => s.Code == styleCode);
        return (style.CategoryNumber, style.CategoryName);
    }

    private protected async Task<Guid> SeedBeerEntryAsync(
        Guid competitionId, Guid participantId, string beerName, string styleCode = StyleCodeApa,
        IEnumerable<string>? collaboratorEmails = null, decimal abvPercent = 0m, Guid? competitionCategoryId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = beerName,
            StyleCode = styleCode,
            BlindCode = NewBlindCode(),
            AbvPercent = abvPercent,
            CompetitionCategoryId = competitionCategoryId,
        };
        db.BeerEntries.Add(entry);
        foreach (var email in collaboratorEmails ?? [])
        {
            db.EntryCollaborators.Add(new EntryCollaborator { BeerEntryId = entry.Id, Email = email });
        }

        await db.SaveChangesAsync();
        return entry.Id;
    }

    private protected async Task<Guid> SeedJudgeAsync(Guid competitionId, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge { CompetitionId = competitionId, Email = email, DisplayName = email.Split('@')[0] };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    private protected async Task<Guid> SeedClosedTableAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable
        {
            CompetitionId = competitionId,
            Name = name,
            State = TableState.Closed,
            ClosedAt = DateTimeOffset.UtcNow,
        };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private protected async Task<bool> GetNotValidForBosAsync(Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.BeerEntries.AsNoTracking().SingleAsync(e => e.Id == beerEntryId);
        return entry.NotValidForBos;
    }
}
