using System.Diagnostics.CodeAnalysis;
using BirraPoint.Api.Common.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.UnitTests.Common.Behaviors;

/// <summary>DI-wiring test: confirms AddOpenBehavior actually applies at runtime (a known MediatR
/// open-generics gotcha) by sending a real request through a real container.</summary>
public sealed class MediatRExtensionsTests
{
    internal sealed record PingRequest(string Message) : IRequest<string>;

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the DI container via MediatR assembly scanning.")]
    internal sealed class PingHandler : IRequestHandler<PingRequest, string>
    {
        public Task<string> Handle(PingRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Task.FromResult($"pong:{request.Message}");
        }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the DI container via MediatR assembly scanning.")]
    internal sealed class PingRequestValidator : AbstractValidator<PingRequest>
    {
        public PingRequestValidator() => RuleFor(r => r.Message).NotEmpty();
    }

    private static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddMediatRWithValidation(typeof(MediatRExtensionsTests).Assembly);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task ResolvesAndSendsARequestThroughTheFullPipeline()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new PingRequest("hello"), TestContext.Current.CancellationToken);

        Assert.Equal("pong:hello", result);
    }

    [Fact]
    public async Task ValidationFailuresShortCircuitTheHandlerViaTheOpenBehavior()
    {
        var mediator = BuildMediator();

        var exception = await Assert.ThrowsAsync<ValidationException>(() => mediator.Send(new PingRequest(""), TestContext.Current.CancellationToken));

        Assert.Contains(exception.Errors, e => e.PropertyName == "Message");
    }
}
