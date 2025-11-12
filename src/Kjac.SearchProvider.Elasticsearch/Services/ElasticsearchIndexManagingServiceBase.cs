using Umbraco.Cms.Core.Sync;

namespace Kjac.SearchProvider.Elasticsearch.Services;

internal abstract class ElasticsearchIndexManagingServiceBase : ElasticsearchServiceBase
{
    private readonly IServerRoleAccessor _serverRoleAccessor;

    protected ElasticsearchIndexManagingServiceBase(IServerRoleAccessor serverRoleAccessor)
        => _serverRoleAccessor = serverRoleAccessor;

    protected bool ShouldNotManipulateIndexes() => _serverRoleAccessor.CurrentServerRole is ServerRole.Subscriber;
}
