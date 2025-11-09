using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Site.Models;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
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
    IContentTypeService contentTypeService,
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

    private IEnumerable<Filter> GetFilters(ArticlesSearchRequest request)
    {
        var articleContentType = contentTypeService.Get("article");
        // only include the "article" document type in the results
        yield return new KeywordFilter(SearchConstants.FieldNames.ContentTypeId, [articleContentType?.Key.ToString() ?? string.Empty], false);

        if (request.Author?.Length > 0)
        {
            yield return new KeywordFilter("authorName", request.Author, false);
        }
        
        if (request.Categories?.Length > 0)
        {
            yield return new KeywordFilter("categoryName", request.Categories, false);
        }

        // articleDate range from query string: ?articleDate=from,to (ISO 8601, comma-separated)
        var articleDateRaw = Request.Query["articleDate"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(articleDateRaw))
        {
            var parts = articleDateRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var fromDto)
                && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var toDto))
            {
                var from = fromDto.UtcDateTime;
                var to = toDto.UtcDateTime;

                if (from > to)
                {
                    // swap if provided in reverse
                    (from, to) = (to, from);
                }

                yield return new DateTimeOffsetRangeFilter(
                    "articleDate",
                    [new DateTimeOffsetRangeFilterRange(from, to)],
                    false);
            }
        }
    }
    
    private static Facet[] GetFacets()
    {
        var facets = new Facet[]
        {
            new KeywordFacet("authorName"),
            new KeywordFacet("categoryName"),
            new DateTimeOffsetRangeFacet("articleDate", new []
            {
                new DateTimeOffsetRangeFacetRange("2023", new DateTime(2023,1,1), new DateTime(2023,12,31)),
                new DateTimeOffsetRangeFacetRange("2024", new DateTime(2024,1,1), new DateTime(2024,12,31)),
                new DateTimeOffsetRangeFacetRange("2025", new DateTime(2025,1,1), new DateTime(2025,12,31)),
            })
        };
        return facets;
    }
    
    private static IEnumerable<Sorter> GetSorters(ArticlesSearchRequest request)
    {
        var direction = request.SortDirection == "asc" ? Direction.Ascending : Direction.Descending;
        Sorter sorter = request.SortBy switch
        {
            "title" => new TextSorter(SearchConstants.FieldNames.Name, direction),
            "date" => new IntegerSorter("articleYear", direction),
            _ => new ScoreSorter(direction)
        };

        return [sorter];
    }

}

