namespace BirraPoint.Api.Domain;

public enum DispatchJobType
{
    GeneratePdfs,
    BundleZip,
    SendResultEmail,
    SendInvitation,

    /// <summary>Creates the judge's Keycloak account without sending an email — split out of the
    /// old combined SendInvitation step (R-20, US14). Enqueued by both provisioning paths
    /// (RegisterJudges/FR-014, ConsolidateJudgeImport/FR-057); SendInvitation is now enqueued only
    /// by the explicit "Notify judges" action (FR-059).</summary>
    ProvisionJudgeAccount,
}
