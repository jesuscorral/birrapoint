using BuildingBlocks.Audit;
using BuildingBlocks.Identity;

namespace BirraPoint.Api.Composition;

/// <summary>Audit actor for the BuildingBlocks.Audit block: the Keycloak subject of the caller (FR-035, FR-039).</summary>
internal sealed class CurrentUserAuditActor(ICurrentUser currentUser) : IAuditActorAccessor
{
    public string ActorUserId => currentUser.Sub;
}
