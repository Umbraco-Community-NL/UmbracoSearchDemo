using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.AzureSearch.Services;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.Models.Configuration;
using Umbraco.Cms.Search.Core.Services;

namespace Umbraco.AzureSearch.NotificationHandlers;

internal sealed class EnsureIndexesNotificationHandler
    : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly IAzureSearchIndexManager _indexManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IndexOptions _indexOptions;
    
    public EnsureIndexesNotificationHandler(
        IAzureSearchIndexManager indexManager,
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
        Type azureSearchIndexerServiceType = typeof(IAzureSearchIndexer);
        
        foreach (IndexRegistration indexRegistration in _indexOptions.GetIndexRegistrations())
        {
            var shouldEnsureIndex = indexRegistration.Indexer == azureSearchIndexerServiceType
                                    || (indexRegistration.Indexer == implicitIndexServiceType &&
                                        defaultIndexServiceType == azureSearchIndexerServiceType);
            
            if (shouldEnsureIndex)
            {
                await _indexManager.EnsureAsync(indexRegistration.IndexAlias);
            }
        }
    }
}
