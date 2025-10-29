using Umbraco.Cms.Core.Sync;

namespace Site.SearchProvider.Services;

internal abstract class AzureSearchIndexManagingServiceBase : AzureSearchServiceBase
{
    private readonly IServerRoleAccessor _serverRoleAccessor;
    
    protected AzureSearchIndexManagingServiceBase(IServerRoleAccessor serverRoleAccessor)
        => _serverRoleAccessor = serverRoleAccessor;
    
    protected bool ShouldNotManipulateIndexes() => _serverRoleAccessor.CurrentServerRole is ServerRole.Subscriber;
}

