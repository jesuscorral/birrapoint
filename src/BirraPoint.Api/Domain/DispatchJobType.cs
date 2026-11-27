namespace BirraPoint.Api.Domain;

/// <summary>Job types handled by the host's <c>IJobHandler</c>s. Stored as the constant's text in the
/// <c>Type</c> column of the job table (<c>BuildingBlocks.Jobs</c> keeps it a string).</summary>
internal static class DispatchJobType
{
    public const string GeneratePdfs = nameof(GeneratePdfs);

    public const string BundleZip = nameof(BundleZip);

    public const string SendResultEmail = nameof(SendResultEmail);

    /// <summary>Regenerates one entry's score sheet and rebuilds the results ZIP after a correction on a
    /// finalized competition (FR-083); sends no emails.</summary>
    public const string RefreshResults = nameof(RefreshResults);

    public const string SendInvitation = nameof(SendInvitation);

    /// <summary>Creates the judge's Keycloak account without sending an email — split out of the
    /// old combined SendInvitation step (R-20, US14). Enqueued by both provisioning paths
    /// (RegisterJudges/FR-014, ConsolidateJudgeImport/FR-057); SendInvitation is now enqueued only
    /// by the explicit "Notify judges" action (FR-059).</summary>
    public const string ProvisionJudgeAccount = nameof(ProvisionJudgeAccount);
}
