using Microsoft.AspNetCore.Mvc;
using Site.Models;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Search.Core.Extensions;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Umbraco.Cms.Search.Core.Services;
using SearchConstants = Umbraco.Cms.Search.Core.Constants;

namespace Site.Controllers;

[ApiController]
public class BooksApiController : ControllerBase
{
    private readonly ISearcherResolver _searcherResolver;
    private readonly IApiContentBuilder _apiContentBuilder;
    private readonly ICacheManager _cacheManager;
    private readonly ILogger<BooksApiController> _logger;

    public BooksApiController(
        ISearcherResolver searcherResolver,
        IApiContentBuilder apiContentBuilder,
        ICacheManager cacheManager,
        ILogger<BooksApiController> logger)
    {
        _searcherResolver = searcherResolver;
        _apiContentBuilder = apiContentBuilder;
        _cacheManager = cacheManager;
        _logger = logger;
    }

    [HttpGet("/api/books")]
    public async Task<IActionResult> GetBooks([FromQuery] BooksSearchRequest request)
    {
        // get the default searcher registered for published content
        var searcher = _searcherResolver.GetRequiredSearcher(SearchConstants.IndexAliases.PublishedContent);

        // get the filters, facets and sorters
        // - filters and sorters are influenced by the active request, facets are fixed
        var filters = GetFilters(request);
        var facets = GetFacets();
        var sorters = GetSorters(request);

        // execute the search request
        var result = await searcher.SearchAsync(
            SearchConstants.IndexAliases.PublishedContent,
            request.Query,
            filters,
            facets,
            sorters,
            culture: null,
            segment: null,
            accessContext: null,
            request.Skip,
            request.Take
        );

        // build response models for the search results (the Delivery API output format)
        var documents = result.Documents
            .Select(document =>
            {
                var publishedContent = _cacheManager.Content.GetById(document.Id);
                if (publishedContent is not null)
                {
                    return _apiContentBuilder.Build(publishedContent);
                }

                _logger.LogWarning("Could not find published content for document with id: {documentId}", document.Id);
                return null;
            })
            .WhereNotNull()
            .ToArray(); 
        
        return Ok(
            new BookSearchResult
            {
                Total = result.Total,
                Facets = result.Facets.ToArray(),
                Documents = documents
            }
        );
    }

    private static IEnumerable<Filter> GetFilters(BooksSearchRequest request)
    {
        
        
        // only include the "book" document type in the results (the document type ID is hardcoded here for simplicity)
        yield return new KeywordFilter("contentTypeAlias", ["article"], false); 
        /*
        var publishYearFilters = (request.PublishYear ?? []).Select(ParseIntegerRangeFilter).WhereNotNull().ToArray();
        if (publishYearFilters.Length is not 0)
        {
            yield return new IntegerRangeFilter("publishYear", publishYearFilters, false); 
        }
        */
        if (request.Author?.Length > 0)
        {
            yield return new KeywordFilter("authorName", request.Author, false);
        }

    }
    
    private static Facet[] GetFacets()
    {
        var facets = new Facet[]
        {
            new KeywordFacet("authorName")
        };
        return facets;
    }
    
    private static IEnumerable<Sorter> GetSorters(BooksSearchRequest request)
    {
        var direction = request.SortDirection == "asc" ? Direction.Ascending : Direction.Descending;
        Sorter sorter = request.SortBy switch
        {
            "title" => new TextSorter(SearchConstants.FieldNames.Name, direction),
            //"publishYear" => new IntegerSorter("publishYear", direction),
            _ => new ScoreSorter(direction)
        };

        return [sorter];
    }

    private static IntegerRangeFilterRange? ParseIntegerRangeFilter(string? filter)
    {
        var values = filter?
            .Split(',')
            .Select(value => int.TryParse(value, out var result)
                ? result
                : (int?)null).ToArray();

        return values?.Length is 2
            ? new IntegerRangeFilterRange(values[0], values[1])
            : null;
    }
}