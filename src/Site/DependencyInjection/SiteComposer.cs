using Site.SearchProvider.Configuration;
using Site.SearchProvider.DependencyInjection;
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
            // use the Azure Search provider
            .AddAzureSearchProvider()
            // force rebuild indexes after startup
            .RebuildIndexesAfterStartup();

        // Configure Azure Search provider to expand facet values (keep all facets visible when filtering)
        builder.Services.Configure<Site.SearchProvider.Configuration.SearcherOptions>(options => options.ExpandFacetValues = true);

        builder
            // configure System.Text.Json to allow serializing output models
            .ConfigureJsonOptions();
    }
}