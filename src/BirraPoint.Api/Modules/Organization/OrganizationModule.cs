using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Organization.Errors;
using BirraPoint.Api.Modules.Organization.Features.Judges;
using BirraPoint.Api.Modules.Organization.Identity;
using BirraPoint.Api.Modules.Organization.Integration;
using BirraPoint.Api.Modules.Organization.Persistence;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
using BuildingBlocks.ProblemDetails;

namespace BirraPoint.Api.Modules.Organization;

internal static class OrganizationModule
{
    /// <summary>Name of Organization's outbox and inbox (the schema-qualified tables are mapped in the module's context).</summary>
    internal const string Messaging = "Organization";

    /// <summary>Registers the module's public contract implementations (scoped: they use the module's scoped context).</summary>
    internal static IServiceCollection AddOrganizationModule(this IServiceCollection services)
    {
        services.AddProblemDetailsMapping(map => map.Map<OrganizationException>(OrganizationException.ToProblem));
        services.AddBirraPointDbContext<OrganizationDbContext>();
        services.AddScoped<IJudgeResolver, JudgeResolver>();
        services.AddScoped<IOrganizerResolver, OrganizerResolver>();
        services.AddScoped<IConflictOfInterestChecker, ConflictOfInterestChecker>();
        services.AddScoped<IJudgeDirectory, JudgeDirectory>();
        services.AddScoped<ICompetitionAccess, CompetitionAccess>();
        services.AddScoped<IEntryDirectory, EntryDirectory>();
        services.AddScoped<IResultRecipients, ResultRecipients>();
        services.AddScoped<BosFlagRecalculator>();

        // SendInvitation is enqueued only by the explicit "Notify judges" action; account provisioning
        // is enqueued eagerly by RegisterJudges and ConsolidateJudgeImport (R-20/US14).
        services.AddScoped<IJobHandler, SendInvitationHandler>();
        services.AddScoped<IJobHandler, ProvisionJudgeAccountHandler>();

        services.ConfigureMessaging(messaging => messaging
            .Outbox<OrganizationDbContext>(Messaging)
            .Inbox<OrganizationDbContext>(Messaging)
            .Publishes<CompetitionFinalized>(Messaging)
            .Publishes<JudgeEmailChanged>(Messaging)
            .Subscribe<TableJudgesChanged, UpdateBosFlagsOnTableJudgesChanged>(Messaging));
        return services;
    }
}
