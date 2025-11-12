using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Kjac.SearchProvider.Elasticsearch.Configuration;
using Microsoft.Extensions.Options;
using BasicAuthentication = Elastic.Transport.BasicAuthentication;

namespace Kjac.SearchProvider.Elasticsearch.Services;

internal sealed class ElasticsearchClientFactory : IElasticsearchClientFactory
{
    private readonly ElasticsearchClient _client;

    public ElasticsearchClientFactory(IOptions<ClientOptions> options)
    {
        ClientOptions elasticClientOptions = options.Value;
        var settings = new ElasticsearchClientSettings(elasticClientOptions.Host);
        if (elasticClientOptions.Authentication?.ApiKey is not null)
        {
            settings.Authentication(
                new ApiKey(elasticClientOptions.Authentication.ApiKey)
            );
        }
        else if (elasticClientOptions.Authentication?.Username is not null
                 && elasticClientOptions.Authentication?.Password is not null)
        {
            settings.Authentication(
                new BasicAuthentication(
                    elasticClientOptions.Authentication.Username,
                    elasticClientOptions.Authentication.Password
                )
            );
        }

        if (elasticClientOptions.EnableDebugMode)
        {
            settings.EnableDebugMode();
        }

        _client = new ElasticsearchClient(settings);
    }

    public ElasticsearchClient GetClient() => _client;
}
