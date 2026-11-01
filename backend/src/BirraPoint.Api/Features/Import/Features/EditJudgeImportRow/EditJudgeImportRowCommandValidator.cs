using FluentValidation;

namespace BirraPoint.Api.Features.Import.Features.EditJudgeImportRow;

/// <summary>
/// Unlike EditBeerImportRowCommandValidator (Features/Import), neither Name nor Email is required at
/// the request level here — a full-replace edit is allowed to leave either missing (Status
/// recomputes to Invalid), per contracts/judge-import-file.md §Full row edit. There is no
/// separate mismatch/unresolved-reference workflow for judge-roster fields (no catalog involved)
/// that would justify forcing a stricter shape the way category/style do for beer entries.
/// </summary>
internal sealed class EditJudgeImportRowCommandValidator : AbstractValidator<EditJudgeImportRowCommand>
{
    public EditJudgeImportRowCommandValidator()
    {
        RuleFor(c => c.Row).NotNull();

        When(c => c.Row is not null, () =>
        {
            RuleFor(c => c.Row.Name).MaximumLength(200);
            RuleFor(c => c.Row.Email).MaximumLength(320);
            RuleFor(c => c.Row.BjcpRank).MaximumLength(100);
            RuleFor(c => c.Row.BjcpId).MaximumLength(50);
            RuleFor(c => c.Row.PreferredCategory).MaximumLength(200);
            RuleFor(c => c.Row.Preferences).MaximumLength(2000);
        });
    }
}
