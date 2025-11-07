using Site.SearchProvider;
using Site.SearchProvider.Configuration;
using Umbraco.AzureSearch.Abstractions;
using Umbraco.AzureSearch.Configuration;
using Umbraco.AzureSearch.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Search.Core.DependencyInjection;
using Umbraco.Cms.Search.Provider.Examine.Configuration;
using SearcherOptions = Site.SearchProvider.Configuration.SearcherOptions;

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
        builder.Services.Configure<SearcherOptions>(options => options.ExpandFacetValues = true);

        // Bind KnownFieldsOptions from configuration:
        // appsettings: "Search:KnownFields"
        builder.Services
            .AddOptions<KnownFieldsOptions>()
            .BindConfiguration("Search:KnownFields");

        // Override the default KnownFields provider with the site-specific, options-based implementation
        builder.Services.AddSingleton<IKnownFieldsProvider, SiteKnownFieldsProvider>();

        // configure System.Text.Json to allow serializing output models
        builder.ConfigureJsonOptions();
    }
}