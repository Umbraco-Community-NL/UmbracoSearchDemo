namespace Kjac.SearchProvider.Elasticsearch.Services;

public interface IElasticsearchIndexManager
{
    Task EnsureAsync(string indexAlias);

    Task ResetAsync(string indexAlias);
}
