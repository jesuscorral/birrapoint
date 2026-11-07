namespace BirraPoint.Api.Modules.Organization.Errors;

/// <summary>Urn/status/title metadata for each <see cref="OrganizationError"/>, per contracts/rest-api.md §Error catalog.</summary>
internal static class OrganizationErrorCatalog
{
    public static readonly IReadOnlyDictionary<OrganizationError, (string Urn, int Status, string Title)> Entries =
        new Dictionary<OrganizationError, (string, int, string)>
        {
            [OrganizationError.InvalidImportFile] = ("urn:birrapoint:invalid-import-file", StatusCodes.Status400BadRequest, "Invalid import file"),
            [OrganizationError.UnresolvedImportRows] = ("urn:birrapoint:unresolved-import-rows", StatusCodes.Status409Conflict, "Unresolved import rows"),
            [OrganizationError.TablesStillOpen] = ("urn:birrapoint:tables-still-open", StatusCodes.Status409Conflict, "Tables still open"),
            [OrganizationError.JudgeAlreadyActive] = ("urn:birrapoint:judge-already-active", StatusCodes.Status409Conflict, "Judge already active"),
            [OrganizationError.InvalidStateTransition] = ("urn:birrapoint:invalid-state-transition", StatusCodes.Status409Conflict, "Invalid state transition"),
        };
}
