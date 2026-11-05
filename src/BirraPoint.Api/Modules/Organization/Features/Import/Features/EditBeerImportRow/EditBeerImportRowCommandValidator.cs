using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.EditBeerImportRow;

/// <summary>
/// ParticipantName/AbvPercent/SubmittedAt have no "unresolved" resolution workflow the way
/// category/style do (data-model.md §BeerImportRow) — a missing/malformed value here is simply a bad
/// request, mirroring SubmitEvaluationCommandValidator's NotNull checks. ParticipantEmail and the
/// category/style references are deliberately NOT required here: an incomplete edit is allowed to
/// save (Status recomputes to Invalid) so the organizer can make partial progress across several
/// saves on the Mapping &amp; Correction screen.
/// </summary>
internal sealed class EditBeerImportRowCommandValidator : AbstractValidator<EditBeerImportRowCommand>
{
    public EditBeerImportRowCommandValidator(AppDbContext dbContext, IStyleCatalog styleCatalog)
    {
        RuleFor(c => c.Row).NotNull();

        When(c => c.Row is not null, () =>
        {
            RuleFor(c => c.Row.ParticipantName).NotEmpty().MaximumLength(200);
            RuleFor(c => c.Row.ParticipantEmail).MaximumLength(320);
            RuleFor(c => c.Row.AcceMemberNumber).MaximumLength(50);
            RuleFor(c => c.Row.Phone).MaximumLength(30);
            RuleFor(c => c.Row.AbvPercent).NotNull().InclusiveBetween(0, 99.99m);
            RuleFor(c => c.Row.SubmittedAt).NotNull();
            RuleFor(c => c.Row.Malts).MaximumLength(1000);
            RuleFor(c => c.Row.Hops).MaximumLength(1000);
            RuleFor(c => c.Row.Yeast).MaximumLength(1000);
            RuleFor(c => c.Row.OtherIngredients).MaximumLength(1000);
            RuleFor(c => c.Row.EntryInstructions).MaximumLength(1000);
            RuleFor(c => c.Row.BeerName).MaximumLength(200);

            RuleFor(c => c)
                .MustAsync(async (command, cancellationToken) =>
                    command.Row.CompetitionCategoryId is null
                    || await dbContext.CompetitionCategories.AnyAsync(
                        category => category.Id == command.Row.CompetitionCategoryId && category.CompetitionId == command.CompetitionId,
                        cancellationToken))
                .WithName(nameof(EditBeerImportRowRequest.CompetitionCategoryId))
                .WithMessage("CompetitionCategoryId does not belong to this competition.");

            RuleFor(c => c)
                .MustAsync(async (command, cancellationToken) =>
                    string.IsNullOrEmpty(command.Row.StyleCode)
                    || (await styleCatalog.GetByCodesAsync([command.Row.StyleCode], cancellationToken)).ContainsKey(command.Row.StyleCode))
                .WithName(nameof(EditBeerImportRowRequest.StyleCode))
                .WithMessage(c => $"Style code '{c.Row.StyleCode}' does not exist in the BJCP 2021 catalog.");
        });
    }
}
