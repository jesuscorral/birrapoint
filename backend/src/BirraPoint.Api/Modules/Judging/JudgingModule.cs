using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Judging.Integration;
using BirraPoint.Api.Modules.Organization.Contracts;

namespace BirraPoint.Api.Modules.Judging;

internal static class JudgingModule
{
    /// <summary>Registers the module's public contract implementations (scoped: they use the scoped DbContext).</summary>
    internal static IServiceCollection AddJudgingModule(this IServiceCollection services)
    {
        services.AddScoped<ICompetitionFinalizationGuard, CompetitionFinalizationGuard>();
        services.AddScoped<IEntryTableAssignments, EntryTableAssignments>();
        services.AddScoped<IFinalizedEvaluations, FinalizedEvaluations>();
        return services;
    }
}
