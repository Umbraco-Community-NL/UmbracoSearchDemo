using Umbraco.Cms.Search.Core.Services;

namespace Kjac.SearchProvider.Elasticsearch.Services;

// public marker interface allowing for explicit index registrations using the Elasticsearch searcher
public interface IElasticsearchSearcher : ISearcher
{
}
