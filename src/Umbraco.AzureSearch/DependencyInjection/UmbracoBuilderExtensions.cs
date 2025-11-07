using Microsoft.Extensions.DependencyInjection;
using Umbraco.AzureSearch.NotificationHandlers;
using Umbraco.AzureSearch.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using CoreConstants = Umbraco.Cms.Search.Core.Constants;

namespace Umbraco.AzureSearch.DependencyInjection;

public static class UmbracoBuilderExtensions
{
    public static IUmbracoBuilder AddAzureSearchProvider(this IUmbracoBuilder builder)
    {
        builder.Services.AddAzureSearch(builder.Config);
        
        builder.Services.Configure<IndexOptions>(
            options =>
            {
                // Register Azure Search index for published content only (for the books demo)
                // Note: Free tier Azure Search limits to 3 total indexes
                options.RegisterIndex<IAzureSearchIndexer, IAzureSearchSearcher, IPublishedContentChangeStrategy>(
                    CoreConstants.IndexAliases.PublishedContent,
                    UmbracoObjectTypes.Document
                );
                
                // Uncomment if you have a paid Azure Search tier with more index capacity:
                // options.RegisterIndex<IAzureSearchIndexer, IAzureSearchSearcher, IDraftContentChangeStrategy>(
                //     CoreConstants.IndexAliases.DraftContent,
                //     UmbracoObjectTypes.Document
                // );
                // options.RegisterIndex<IAzureSearchIndexer, IAzureSearchSearcher, IDraftContentChangeStrategy>(
                //     CoreConstants.IndexAliases.DraftMedia,
                //     UmbracoObjectTypes.Media
                // );
                // options.RegisterIndex<IAzureSearchIndexer, IAzureSearchSearcher, IDraftContentChangeStrategy>(
                //     CoreConstants.IndexAliases.DraftMembers,
                //     UmbracoObjectTypes.Member
                // );
            }
        );
        
        // Ensure all indexes exist before Umbraco has finished start-up
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, EnsureIndexesNotificationHandler>();
        
        return builder;
    }
}

