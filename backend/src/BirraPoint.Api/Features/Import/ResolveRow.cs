using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Import;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ResolveRowCommand(Guid CompetitionId, Guid ImportId, int RowNumber, string Action, string? StyleCode)
    : IRequest<ImportRowDto?>;

public sealed class ResolveRowCommandValidator : AbstractValidator<ResolveRowCommand>
{
    public ResolveRowCommandValidator(AppDbContext dbContext)
    {
        RuleFor(c => c.Action)
            .Must(action => action is "assign-style" or "exclude")
            .WithMessage("Action must be 'assign-style' or 'exclude'.");

        RuleFor(c => c.StyleCode)
            .NotEmpty()
            .WithMessage("StyleCode is required for the assign-style action.")
            .DependentRules(() =>
                RuleFor(c => c.StyleCode)
                    .MustAsync((styleCode, cancellationToken) =>
                        dbContext.BjcpStyles.AnyAsync(style => style.Code == styleCode, cancellationToken))
                    .WithMessage(c => $"Style code '{c.StyleCode}' does not exist in the BJCP 2021 catalog."))
            .When(c => c.Action == "assign-style");
    }
}

public sealed class ResolveRowCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ResolveRowCommand, ImportRowDto?>
{
    public async Task<ImportRowDto?> Handle(ResolveRowCommand request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var batchExists = await dbContext.ImportBatches
            .AnyAsync(b => b.Id == request.ImportId && b.CompetitionId == request.CompetitionId, cancellationToken);

        if (!batchExists)
        {
            return null;
        }

        var row = await dbContext.ImportRows
            .FirstOrDefaultAsync(
                r => r.ImportBatchId == request.ImportId && r.RowNumber == request.RowNumber, cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (request.Action == "assign-style")
        {
            row.ResolvedStyleCode = request.StyleCode;
            row.Status = ImportRowStatus.Valid;
            row.ErrorMessage = null;
        }
        else
        {
            row.Status = ImportRowStatus.Excluded;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ImportRowDto.FromEntity(row);
    }
}
