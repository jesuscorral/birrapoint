namespace BirraPoint.Api.Common.Errors;

public static class ErrorHandlingExtensions
{
    /// <summary>
    /// Registers ProblemDetails + the exception handler chain (T012). Handlers run in
    /// registration order until one returns true; <see cref="FallbackExceptionHandler"/> must
    /// stay last so it only catches what the more specific handlers didn't.
    /// </summary>
    public static IServiceCollection AddProblemDetailsErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<FallbackExceptionHandler>();
        return services;
    }
}
