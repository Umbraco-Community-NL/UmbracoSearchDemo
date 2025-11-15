using Kjac.SearchProvider.Elasticsearch.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Search.Core.DependencyInjection;

namespace Site.DependencyInjection;

public class SiteComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder
            // add core services for search abstractions
            .AddSearchCore()
            // use the Elastic search provider
            .AddElasticsearchSearchProvider();
        
        // force rebuild indexes after startup
        builder.RebuildIndexesAfterStartup();

        builder
            // configure System.Text.Json to allow serializing output models
            .ConfigureJsonOptions();
    }
}