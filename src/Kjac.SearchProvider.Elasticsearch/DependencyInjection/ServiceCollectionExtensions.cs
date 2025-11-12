using Kjac.SearchProvider.Elasticsearch.Configuration;
using Kjac.SearchProvider.Elasticsearch.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Search.Core.Services;

namespace Kjac.SearchProvider.Elasticsearch.DependencyInjection;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElasticsearch(this IServiceCollection services, IConfiguration configuration)
    {
        // register the Elasticsearch searcher and indexer so they can be used explicitly for index registrations
        services.AddTransient<IElasticsearchIndexer, ElasticsearchIndexer>();
        services.AddTransient<IElasticsearchSearcher, ElasticsearchSearcher>();

        // register the Elasticsearch searcher and indexer as the defaults
        services.AddTransient<IIndexer, ElasticsearchIndexer>();
        services.AddTransient<ISearcher, ElasticsearchSearcher>();

        // register supporting services
        services.AddSingleton<IElasticsearchClientFactory, ElasticsearchClientFactory>();
        services.AddSingleton<IElasticsearchIndexManager, ElasticsearchIndexManager>();
        services.AddSingleton<IIndexAliasResolver, IndexAliasResolver>();

        services.Configure<ClientOptions>(configuration.GetSection("ElasticsearchSearchProvider:Client"));

        return services;
    }
}
