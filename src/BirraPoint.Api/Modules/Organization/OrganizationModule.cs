using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Errors;
using BirraPoint.Api.Modules.Organization.Features.Judges;
using BirraPoint.Api.Modules.Organization.Integration;
using BuildingBlocks.ProblemDetails;

namespace BirraPoint.Api.Modules.Organization;

internal static class OrganizationModule
{
    /// <summary>Registers the module's public contract implementations (scoped: they use the scoped DbContext).</summary>
    internal static IServiceCollection AddOrganizationModule(this IServiceCollection services)
    {
        services.AddProblemDetailsMapping(map => map.Map<OrganizationException>(OrganizationException.ToProblem));
        services.AddScoped<IConflictOfInterestChecker, ConflictOfInterestChecker>();
        services.AddScoped<IJudgeDirectory, JudgeDirectory>();
        services.AddScoped<ICompetitionAccess, CompetitionAccess>();
        services.AddScoped<IEntryDirectory, EntryDirectory>();
        services.AddScoped<IResultRecipients, ResultRecipients>();

        // SendInvitation is enqueued only by the explicit "Notify judges" action; account provisioning
        // is enqueued eagerly by RegisterJudges and ConsolidateJudgeImport (R-20/US14).
        services.AddScoped<IDispatchJobHandler, SendInvitationHandler>();
        services.AddScoped<IDispatchJobHandler, ProvisionJudgeAccountHandler>();
        return services;
    }
}
