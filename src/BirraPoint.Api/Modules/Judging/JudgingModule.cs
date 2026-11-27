using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Judging.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Judging.Integration;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;
using BuildingBlocks.Messaging;
using BuildingBlocks.ProblemDetails;

namespace BirraPoint.Api.Modules.Judging;

internal static class JudgingModule
{
    /// <summary>Name of Judging's outbox and inbox (the schema-qualified tables are mapped in the host DbContext).</summary>
    internal const string Messaging = "Judging";

    /// <summary>Registers the module's public contract implementations (scoped: they use the scoped DbContext)
    /// and its integration events: what it publishes and what it subscribes to.</summary>
    internal static IServiceCollection AddJudgingModule(this IServiceCollection services)
    {
        services.AddProblemDetailsMapping(map => map.Map<JudgingException>(JudgingException.ToProblem));
        services.AddScoped<ICompetitionFinalizationGuard, CompetitionFinalizationGuard>();
        services.AddScoped<IEntryTableAssignments, EntryTableAssignments>();
        services.AddScoped<IStoredEvaluations, StoredEvaluations>();
        services.AddScoped<IJudgeEngagement, JudgeEngagement>();

        services.ConfigureMessaging(messaging => messaging
            .Publishes<TableJudgesChanged>(Messaging)
            .Publishes<EvaluationCorrected>(Messaging)
            .Subscribe<JudgeEmailChanged, AlertOrganizersOnJudgeConflict>(Messaging));
        return services;
    }
}
