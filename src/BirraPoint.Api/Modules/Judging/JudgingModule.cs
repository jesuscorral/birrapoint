using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Judging.Integration;
using BirraPoint.Api.Modules.Judging.Persistence;
using BirraPoint.Contracts.Judging;
using BirraPoint.Contracts.Judging.IntegrationEvents;
using BirraPoint.Contracts.Organization;
using BirraPoint.Contracts.Organization.IntegrationEvents;
using BuildingBlocks.Messaging;
using BuildingBlocks.ProblemDetails;

namespace BirraPoint.Api.Modules.Judging;

internal static class JudgingModule
{
    /// <summary>Name of Judging's outbox and inbox (the schema-qualified tables are mapped in the module's context).</summary>
    internal const string Messaging = "Judging";

    /// <summary>Registers the module's public contract implementations (scoped: they use the module's scoped context)
    /// and its integration events: what it publishes and what it subscribes to.</summary>
    internal static IServiceCollection AddJudgingModule(this IServiceCollection services)
    {
        services.AddProblemDetailsMapping(map => map.Map<JudgingException>(JudgingException.ToProblem));
        services.AddBirraPointDbContext<JudgingDbContext>();
        services.AddScoped<ICompetitionFinalizationGuard, CompetitionFinalizationGuard>();
        services.AddScoped<IEntryTableAssignments, EntryTableAssignments>();
        services.AddScoped<IStoredEvaluations, StoredEvaluations>();
        services.AddScoped<IJudgeEngagement, JudgeEngagement>();
        services.AddScoped<ITableMembership, TableMembership>();

        services.ConfigureMessaging(messaging => messaging
            .Outbox<JudgingDbContext>(Messaging)
            .Inbox<JudgingDbContext>(Messaging)
            .Publishes<TableJudgesChanged>(Messaging)
            .Publishes<EvaluationCorrected>(Messaging)
            .Subscribe<JudgeEmailChanged, AlertOrganizersOnJudgeConflict>(Messaging));
        return services;
    }
}
