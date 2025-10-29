using Umbraco.Cms.Search.Core.Services;

namespace Site.SearchProvider.Services;

// public marker interface allowing for explicit index registrations using the Azure Search searcher
public interface IAzureSearchSearcher : ISearcher
{
}

