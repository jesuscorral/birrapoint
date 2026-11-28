using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SecondAssembly = BuildingBlocks.Validation.MediatR.Tests.SecondAssembly;

namespace BuildingBlocks.Validation.MediatR.Tests;

/// <summary>Each module is its own assembly (FR-102), so the host registers handlers and validators from several.</summary>
public sealed class MultiAssemblyRegistrationTests
{
    private static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddMediatRWithValidation(
            typeof(MediatRExtensionsTests).Assembly,
            typeof(SecondAssembly.SecondAssemblyRequest).Assembly);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task HandlesARequestFromTheSecondAssembly()
    {
        var result = await BuildMediator().Send(new SecondAssembly.SecondAssemblyRequest("hi"), TestContext.Current.CancellationToken);

        Assert.Equal("second:hi", result);
    }

    [Fact]
    public async Task StillHandlesARequestFromTheFirstAssembly()
    {
        var result = await BuildMediator().Send(new MediatRExtensionsTests.PingRequest("hi"), TestContext.Current.CancellationToken);

        Assert.Equal("pong:hi", result);
    }

    [Fact]
    public async Task ValidatesARequestFromTheSecondAssemblyThroughThePipeline()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => BuildMediator().Send(new SecondAssembly.SecondAssemblyRequest(""), TestContext.Current.CancellationToken));

        Assert.Contains(exception.Errors, e => e.PropertyName == "Message");
    }
}
