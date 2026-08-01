using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Import;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record RevalidateImportCommand(Guid CompetitionId, Guid ImportId) : IRequest<ImportBatchDto?>;

/// <summary>
/// FR-054: re-runs category/style/allow-list resolution (import-file.md §Revalidation) for every
/// row currently Valid/StyleMismatch/CategoryMismatch/CategoryStyleMismatch, against the
/// competition's *current* categories and style assignments. Needed because
/// <c>SetCompetitionCategoriesCommandHandler</c> is a full-replace PUT — it deletes and recreates
/// CompetitionCategory/CompetitionCategoryStyle rows, so a previously-resolved
/// ResolvedCompetitionCategoryId can go stale (point at a deleted id) even when a same-named
/// category still exists. Lets the organizer fix a category/style-assignment problem in wizard
/// step 3 and pick the import back up without re-uploading the file. No-op once the batch is
/// Consolidated (nothing left to fix). Invalid/Excluded rows are skipped — their status is
/// unrelated to category/style state.
/// </summary>
public sealed class RevalidateImportCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<RevalidateImportCommand, ImportBatchDto?>
{
    public async Task<ImportBatchDto?> Handle(RevalidateImportCommand request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var batch = await dbContext.ImportBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == request.ImportId && b.CompetitionId == request.CompetitionId, cancellationToken);

        if (batch is null)
        {
            return null;
        }

        if (batch.Status != ImportBatchStatus.Pending)
        {
            return ImportBatchDto.FromEntity(batch);
        }

        var categories = await dbContext.CompetitionCategories
            .Where(category => category.CompetitionId == batch.CompetitionId)
            .Select(category => new CategoryCatalogEntry(category.Id, category.Name))
            .ToListAsync(cancellationToken);

        var styles = await dbContext.BjcpStyles
            .Select(style => new StyleCatalogEntry(style.Code, style.Name))
            .ToListAsync(cancellationToken);

        var allowedPairs = await dbContext.CompetitionCategoryStyles
            .Where(pair => pair.CompetitionId == batch.CompetitionId)
            .Select(pair => new CategoryStyleCatalogEntry(pair.CompetitionCategoryId, pair.StyleCode))
            .ToListAsync(cancellationToken);

        var categoriesById = categories.ToDictionary(category => category.Id);
        var styleCodes = new HashSet<string>(styles.Select(style => style.Code), StringComparer.OrdinalIgnoreCase);

        foreach (var row in batch.Rows)
        {
            if (row.Status is not (ImportRowStatus.Valid or ImportRowStatus.StyleMismatch
                or ImportRowStatus.CategoryMismatch or ImportRowStatus.CategoryStyleMismatch))
            {
                continue;
            }

            var resolvedCategoryId = row.ResolvedCompetitionCategoryId is { } currentCategoryId && categoriesById.ContainsKey(currentCategoryId)
                ? currentCategoryId
                : WorkbookParser.MatchCategory(row.CategoryText, categories);

            if (resolvedCategoryId is null)
            {
                // Category unresolved — leave a previously-picked style untouched (import-file.md
                // §Revalidation): it may still be valid once the category itself gets fixed.
                row.ResolvedCompetitionCategoryId = null;
                row.Status = ImportRowStatus.CategoryMismatch;
                row.ErrorMessage = $"Categoria '{row.CategoryText}' does not match any category configured for this competition.";
                continue;
            }

            row.ResolvedCompetitionCategoryId = resolvedCategoryId;

            var resolvedStyleCode = row.ResolvedStyleCode is { } currentStyleCode && styleCodes.Contains(currentStyleCode)
                ? currentStyleCode
                : WorkbookParser.MatchStyleCode(row.StyleText, styles);

            if (resolvedStyleCode is null)
            {
                row.ResolvedStyleCode = null;
                row.Status = ImportRowStatus.StyleMismatch;
                row.ErrorMessage = $"Estilo '{row.StyleText}' does not match any BJCP 2021 catalog code or name.";
                continue;
            }

            row.ResolvedStyleCode = resolvedStyleCode;

            var isAllowedPair = allowedPairs.Any(pair =>
                pair.CategoryId == resolvedCategoryId.Value
                && string.Equals(pair.StyleCode, resolvedStyleCode, StringComparison.OrdinalIgnoreCase));

            if (isAllowedPair)
            {
                row.Status = ImportRowStatus.Valid;
                row.ErrorMessage = null;
            }
            else
            {
                var categoryName = categoriesById[resolvedCategoryId.Value].Name;
                row.Status = ImportRowStatus.CategoryStyleMismatch;
                row.ErrorMessage =
                    $"Estilo '{row.StyleText}' ({resolvedStyleCode}) is a valid BJCP style, but is not assigned to category '{categoryName}' in this competition.";
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ImportBatchDto.FromEntity(batch);
    }
}
