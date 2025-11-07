using Umbraco.Cms.Core.Sync;

namespace Umbraco.AzureSearch.Services;

internal abstract class AzureSearchIndexManagingServiceBase : AzureSearchServiceBase
{
    private readonly IServerRoleAccessor _serverRoleAccessor;
    
    protected AzureSearchIndexManagingServiceBase(IServerRoleAccessor serverRoleAccessor)
        => _serverRoleAccessor = serverRoleAccessor;
    
    protected bool ShouldNotManipulateIndexes() => _serverRoleAccessor.CurrentServerRole is ServerRole.Subscriber;
}

