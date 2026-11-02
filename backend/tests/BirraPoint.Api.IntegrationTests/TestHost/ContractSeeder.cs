using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>Direct-to-database seeding for tests that resolve cross-module contracts from the
/// service provider instead of going through HTTP.</summary>
internal sealed class ContractSeeder(IServiceProvider services)
{
    public const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    public async Task<Guid> CompetitionAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var competition = new Competition
        {
            Name = $"Contract {Guid.NewGuid():N}",
            Venue = "Venue",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 3),
            CreatedByUserId = $"organizer-{Guid.NewGuid():N}",
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();
        return competition.Id;
    }

    public async Task<Guid> EntryAsync(Guid competitionId, string participantEmail, string[]? collaboratorEmails = null)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = "Entrant Name", Email = participantEmail };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();

        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participant.Id,
            BeerName = "Beer",
            StyleCode = "21A",
            BlindCode = $"B{Guid.NewGuid():N}"[..8],
        };
        foreach (var email in collaboratorEmails ?? [])
        {
            entry.Collaborators.Add(new EntryCollaborator { Email = email });
        }

        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    public async Task<Guid> JudgeAsync(Guid competitionId, string email, string? displayName = null)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge
        {
            CompetitionId = competitionId,
            Email = email,
            DisplayName = displayName ?? email.Split('@')[0],
            KeycloakUserId = $"kc-{Guid.NewGuid():N}",
        };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    public async Task<Guid> TableAsync(Guid competitionId, string name, TableState state = TableState.Open)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable { CompetitionId = competitionId, Name = name, State = state };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    public async Task SampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    public async Task EvaluationAsync(
        Guid tableId, Guid judgeId, Guid beerEntryId, string? descriptorsJson = null, string? feedback = null,
        EvaluationStatus status = EvaluationStatus.Confirmed)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Evaluations.Add(new Evaluation
        {
            TastingTableId = tableId,
            JudgeId = judgeId,
            BeerEntryId = beerEntryId,
            AromaScore = 10,
            AppearanceScore = 2,
            FlavorScore = 15,
            MouthfeelScore = 4,
            OverallScore = 8,
            AromaComment = LongComment,
            AppearanceComment = LongComment,
            FlavorComment = LongComment,
            MouthfeelComment = LongComment,
            OverallComment = LongComment,
            DescriptorsJson = descriptorsJson,
            FeedbackComment = feedback,
            Status = status,
            SubmittedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
