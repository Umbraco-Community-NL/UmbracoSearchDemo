namespace Kjac.SearchProvider.Elasticsearch.Services;

public interface IIndexAliasResolver
{
    string Resolve(string indexAlias);
}
