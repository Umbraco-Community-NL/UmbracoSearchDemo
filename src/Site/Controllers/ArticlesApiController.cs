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
public class ArticlesApiController(
    ISearcherResolver searcherResolver,
    IApiContentBuilder apiContentBuilder,
    ICacheManager cacheManager,
    ILogger<ArticlesApiController> logger)
    : ControllerBase
{
    [HttpGet("/api/articles")]
    public async Task<IActionResult> GetArticles([FromQuery] ArticlesSearchRequest request)
    {
        // get the default searcher registered for published content
        var searcher = searcherResolver.GetRequiredSearcher(SearchConstants.IndexAliases.PublishedContent);

        // get the filters, facets and sorters
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
                var publishedContent = cacheManager.Content.GetById(document.Id);
                if (publishedContent is not null)
                {
                    return apiContentBuilder.Build(publishedContent);
                }

                logger.LogWarning("Could not find published content for document with id: {documentId}", document.Id);
                return null;
            })
            .WhereNotNull()
            .ToArray(); 
        
        return Ok(
            new ArticleSearchResult
            {
                Total = result.Total,
                Facets = result.Facets.ToArray(),
                Documents = documents
            }
        );
    }

    private static IEnumerable<Filter> GetFilters(ArticlesSearchRequest request)
    {
        // only include the "article" document type in the results
        yield return new KeywordFilter("contentTypeAlias", ["article"], false); 
        
        if (request.Author?.Length > 0)
        {
            yield return new KeywordFilter("authorName", request.Author, false);
        }
        
        if (request.Categories?.Length > 0)
        {
            yield return new KeywordFilter("categoryName", request.Categories, false);
        }
        
        if (request.ArticleYear?.Length > 0)
        {
            var parsedYears = request.ArticleYear
                .Select(year => int.TryParse(year, out var result) ? (int?)result : null)
                .Where(year => year.HasValue)
                .Select(year => year!.Value)
                .Distinct()
                .OrderBy(year => year)
                .ToArray();
                
            if (parsedYears.Length > 0)
            {
                var minYear = parsedYears.Min();
                var maxYear = parsedYears.Max();
                
                
                yield return new IntegerRangeFilter("articleYear", new []
                {
                    new IntegerRangeFilterRange(minYear, maxYear)
                }, false);

            }
        }
    }
    
    private static Facet[] GetFacets()
    {
        var facets = new Facet[]
        {
            new KeywordFacet("authorName"),
            new KeywordFacet("categoryName"),
            new IntegerExactFacet("articleYear")
        };
        return facets;
    }
    
    private static IEnumerable<Sorter> GetSorters(ArticlesSearchRequest request)
    {
        var direction = request.SortDirection == "asc" ? Direction.Ascending : Direction.Descending;
        Sorter sorter = request.SortBy switch
        {
            "title" => new TextSorter(SearchConstants.FieldNames.Name, direction),
            "date" => new TextSorter("articleYear", direction),
            _ => new ScoreSorter(direction)
        };

        return [sorter];
    }
}

