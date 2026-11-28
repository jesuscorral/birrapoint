using FluentValidation;
using MediatR;

namespace BuildingBlocks.Validation.MediatR.Tests.SecondAssembly;

/// <summary>A request that lives outside the test assembly, to prove registration scans every assembly it is given.</summary>
public sealed record SecondAssemblyRequest(string Message) : IRequest<string>;

public sealed class SecondAssemblyHandler : IRequestHandler<SecondAssemblyRequest, string>
{
    public Task<string> Handle(SecondAssemblyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult($"second:{request.Message}");
    }
}

public sealed class SecondAssemblyRequestValidator : AbstractValidator<SecondAssemblyRequest>
{
    public SecondAssemblyRequestValidator() => RuleFor(r => r.Message).NotEmpty();
}
