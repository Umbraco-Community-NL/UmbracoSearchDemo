using Azure;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.AzureSearch.Configuration;
using Umbraco.AzureSearch.Services;
using Umbraco.Cms.Search.Core.Services;

namespace Umbraco.AzureSearch.DependencyInjection;

internal static class ServiceCollectionExtensions
{
                public static IServiceCollection AddAzureSearch(this IServiceCollection services, IConfiguration configuration)
                {
                    // Configure client options
                    services.Configure<ClientOptions>(configuration.GetSection("AzureSearchProvider:Client"));
                    
                    // Get client configuration for creating Azure Search clients
                    var clientOptions = new ClientOptions();
                    IConfigurationSection clientConfiguration = configuration.GetSection("AzureSearchProvider:Client");
                    if (clientConfiguration.Exists())
                    {
                        clientConfiguration.Bind(clientOptions);
                    }
                    
                    if (string.IsNullOrEmpty(clientOptions.ServiceName) || string.IsNullOrEmpty(clientOptions.ApiKey))
                    {
                        throw new InvalidOperationException("Azure Search provider configuration is missing or incomplete. Please configure ServiceName and ApiKey.");
                    }
                    
                    // Create Azure Search endpoint and credentials
                    var searchEndpoint = new Uri($"https://{clientOptions.ServiceName}.search.windows.net");
                    var credential = new AzureKeyCredential(clientOptions.ApiKey);
                    
                    // Register SearchIndexClient for index management
                    services.AddSingleton(new SearchIndexClient(searchEndpoint, credential));
                    
                    // Register the Azure Search searcher and indexer so they can be used explicitly for index registrations
                    services.AddTransient<IAzureSearchIndexer, AzureSearchIndexer>();
                    services.AddTransient<IAzureSearchSearcher, AzureSearchSearcher>();
                    
                    // Register the Azure Search searcher and indexer as the defaults
                    services.AddTransient<IIndexer, AzureSearchIndexer>();
                    services.AddTransient<ISearcher, AzureSearchSearcher>();
                    
                    // Register supporting services
                    services.AddSingleton<IAzureSearchIndexManager, AzureSearchIndexManager>();
                    services.AddSingleton<IIndexAliasResolver, IndexAliasResolver>();
                    
                    return services;
                }
}

