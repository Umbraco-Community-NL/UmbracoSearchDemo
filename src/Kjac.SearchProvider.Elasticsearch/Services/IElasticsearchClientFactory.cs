using Elastic.Clients.Elasticsearch;

namespace Kjac.SearchProvider.Elasticsearch.Services;

public interface IElasticsearchClientFactory
{
    ElasticsearchClient GetClient();
}
