using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Services;
using Kjac.SearchProvider.Elasticsearch.Services;
using Umbraco.Cms.Search.Core.Models.Configuration;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Kjac.SearchProvider.Elasticsearch.NotificationHandlers;

internal sealed class EnsureIndexesNotificationHandler
    : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly IElasticsearchIndexManager _indexManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IndexOptions _indexOptions;

    public EnsureIndexesNotificationHandler(
        IElasticsearchIndexManager indexManager,
        IServiceProvider serviceProvider,
        IOptions<IndexOptions> indexOptions)
    {
        _indexManager = indexManager;
        _serviceProvider = serviceProvider;
        _indexOptions = indexOptions.Value;
    }

    public async Task HandleAsync(
        UmbracoApplicationStartingNotification notification,
        CancellationToken cancellationToken)
    {
        Type implicitIndexServiceType = typeof(IIndexer);
        Type defaultIndexServiceType = _serviceProvider.GetRequiredService<IIndexer>().GetType();
        Type elasticsearchIndexerServiceType = typeof(IElasticsearchIndexer);

        foreach (IndexRegistration indexRegistration in _indexOptions.GetIndexRegistrations())
        {
            var shouldEnsureIndex = indexRegistration.Indexer == elasticsearchIndexerServiceType
                                    || (indexRegistration.Indexer == implicitIndexServiceType &&
                                        defaultIndexServiceType == elasticsearchIndexerServiceType);

            if (shouldEnsureIndex)
            {
                await _indexManager.EnsureAsync(indexRegistration.IndexAlias);
            }
        }
    }
}
