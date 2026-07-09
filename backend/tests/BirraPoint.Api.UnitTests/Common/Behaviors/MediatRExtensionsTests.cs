using BirraPoint.Api.Common.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.UnitTests.Common.Behaviors;

/// <summary>DI-wiring test: confirms AddOpenBehavior actually applies at runtime (a known MediatR
/// open-generics gotcha) by sending a real request through a real container.</summary>
public sealed class MediatRExtensionsTests
{
    public sealed record PingRequest(string Message) : IRequest<string>;

    public sealed class PingHandler : IRequestHandler<PingRequest, string>
    {
        public Task<string> Handle(PingRequest request, CancellationToken cancellationToken) =>
            Task.FromResult($"pong:{request.Message}");
    }

    public sealed class PingRequestValidator : AbstractValidator<PingRequest>
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
    public async Task Resolves_and_sends_a_request_through_the_full_pipeline()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new PingRequest("hello"));

        Assert.Equal("pong:hello", result);
    }

    [Fact]
    public async Task Validation_failures_short_circuit_the_handler_via_the_open_behavior()
    {
        var mediator = BuildMediator();

        var exception = await Assert.ThrowsAsync<ValidationException>(() => mediator.Send(new PingRequest("")));

        Assert.Contains(exception.Errors, e => e.PropertyName == "Message");
    }
}
