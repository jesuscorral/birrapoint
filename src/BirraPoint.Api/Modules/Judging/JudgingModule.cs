using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Judging.Integration;
using BirraPoint.Api.Modules.Organization.Contracts;
using BuildingBlocks.ProblemDetails;

namespace BirraPoint.Api.Modules.Judging;

internal static class JudgingModule
{
    /// <summary>Registers the module's public contract implementations (scoped: they use the scoped DbContext).</summary>
    internal static IServiceCollection AddJudgingModule(this IServiceCollection services)
    {
        services.AddProblemDetailsMapping(map => map.Map<JudgingException>(JudgingException.ToProblem));
        services.AddScoped<ICompetitionFinalizationGuard, CompetitionFinalizationGuard>();
        services.AddScoped<IEntryTableAssignments, EntryTableAssignments>();
        services.AddScoped<IStoredEvaluations, StoredEvaluations>();
        services.AddScoped<IJudgeEngagement, JudgeEngagement>();
        return services;
    }
}
