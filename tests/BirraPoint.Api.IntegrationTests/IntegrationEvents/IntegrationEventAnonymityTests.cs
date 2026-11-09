using System.Net;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// Integration events carry identifiers only (integration-events.md, FR-085, BR-01): the stored payload of every
/// event, and in particular the ones Judging consumes, has no entrant or personal field and no value taken from one.
/// The payloads are inspected as stored, so the check holds for whatever transport delivers them later.
/// </summary>
public sealed class IntegrationEventAnonymityTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    private const string SecretParticipant = "Zythos Brauerei Secreta";
    private const string SecretBeer = "Secret Stout Reserva";
    private const string SecretBrewery = "Brauerei Zythos";

    private static readonly string[] ForbiddenKeyFragments =
        ["name", "participant", "brewery", "origin", "email", "country", "address", "city", "phone"];

    private static readonly Dictionary<string, string[]> AllowedKeys = new(StringComparer.Ordinal)
    {
        [MessagingDb.CompetitionFinalized] = ["competitionid"],
        [MessagingDb.JudgeEmailChanged] = ["competitionid", "judgeid"],
        [MessagingDb.TableJudgesChanged] = ["competitionid", "tableid", "addedjudgeids", "removedjudgeids"],
        [MessagingDb.EvaluationCorrected] = ["competitionid", "beerentryid", "evaluationid"],
    };

    [Fact]
    public async Task EveryStoredEventPayloadHasOnlyIdentifierFieldsAndNoEntrantOrPersonalData()
    {
        var secretEmail = NewEmail("secret-brewer");
        var competitionIds = new List<Guid>();

        await using (var host = NewHost(dispatcherEnabled: false))
        {
            using var organizer = OrganizerClient(host, NewOrganizerSub());
            var services = host.Services;

            // competition-finalized and evaluation-corrected on a finalized competition.
            var closed = await SeedClosedCompetitionAsync(host, organizer, "Anonymity");
            await MessagingDb.ExecuteAsync(
                ConnectionString,
                "UPDATE organization.\"Participants\" SET \"Name\" = @name, \"Email\" = @email WHERE \"Id\" = @id",
                ("name", SecretParticipant), ("email", secretEmail), ("id", closed.Entries[0].ParticipantId));
            await MessagingDb.ExecuteAsync(
                ConnectionString,
                "UPDATE organization.\"BeerEntries\" SET \"BeerName\" = @beer WHERE \"Id\" = @id",
                ("beer", SecretBeer), ("id", closed.Entries[0].EntryId));
            Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(organizer, closed.CompetitionId, "Finalized")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await CorrectEvaluationAsync(organizer, closed.CompetitionId, closed.Entries[0].EvaluationId)).StatusCode);
            competitionIds.Add(closed.CompetitionId);

            // table-judges-changed and judge-email-changed.
            var competitionId = await CreateCompetitionAsync(organizer, "AnonymityTables");
            var participantId = await SeedParticipantAsync(services, competitionId, SecretParticipant, secretEmail);
            var entryId = await SeedEntryAsync(services, competitionId, participantId, SecretBeer);
            var judgeId = await SeedJudgeAsync(services, competitionId, NewEmail("judge"));
            var tableName = $"Table {Guid.NewGuid():N}";
            var tableId = await ReadIdAsync(await CreateTableAsync(organizer, competitionId, tableName, [judgeId], []));
            Assert.Equal(HttpStatusCode.OK, (await UpdateTableAsync(organizer, competitionId, tableId, tableName, [judgeId], [entryId])).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await UpdateJudgeEmailAsync(organizer, competitionId, judgeId, secretEmail)).StatusCode);
            competitionIds.Add(competitionId);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, allowed) in AllowedKeys)
        {
            var schema = type.StartsWith("organization.", StringComparison.Ordinal) ? MessagingDb.Organization : MessagingDb.Judging;
            var rows = await MessagingDb.OutboxAsync(ConnectionString, schema, type);
            foreach (var row in rows.Where(r => competitionIds.Contains(r.Property("competitionId").GetGuid())))
            {
                seen.Add(type);
                AssertIdentifiersOnly(row, allowed);
            }
        }

        Assert.Equal(AllowedKeys.Keys.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    private static void AssertIdentifiersOnly(OutboxRow row, string[] allowedKeys)
    {
        var text = row.Payload.GetRawText();
        foreach (var secret in new[] { SecretParticipant, SecretBeer, SecretBrewery, "@brew.example" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var property in row.Payload.EnumerateObject())
        {
            var key = property.Name;
            Assert.Contains(allowedKeys, allowed => string.Equals(allowed, key, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(ForbiddenKeyFragments, fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            AssertOnlyGuids(row.Type, property.Value);
        }
    }

    private static void AssertOnlyGuids(string type, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    AssertOnlyGuids(type, item);
                }

                break;
            case JsonValueKind.String:
                Assert.True(value.TryGetGuid(out _), $"{type} payload carries a non-identifier value.");
                break;
            default:
                Assert.Fail($"{type} payload carries a {value.ValueKind} value; only identifiers are allowed.");
                break;
        }
    }
}
