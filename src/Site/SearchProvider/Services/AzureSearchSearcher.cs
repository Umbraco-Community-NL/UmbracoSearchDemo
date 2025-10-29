using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Site.SearchProvider.Configuration;
using Site.SearchProvider.Constants;
using Site.SearchProvider.Extensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Umbraco.Extensions;
using AzureFacetResult = Azure.Search.Documents.Models.FacetResult;
using UmbracoFacetResult = Umbraco.Cms.Search.Core.Models.Searching.Faceting.FacetResult;

namespace Site.SearchProvider.Services;

internal sealed class AzureSearchSearcher : AzureSearchServiceBase, IAzureSearchSearcher
{
    private readonly SearchIndexClient _indexClient;
    private readonly IIndexAliasResolver _indexAliasResolver;
    private readonly SearcherOptions _searcherOptions;
    private readonly ILogger<AzureSearchSearcher> _logger;
    
    public AzureSearchSearcher(
        SearchIndexClient indexClient,
        IIndexAliasResolver indexAliasResolver,
        IOptions<SearcherOptions> options,
        ILogger<AzureSearchSearcher> logger)
    {
        _indexClient = indexClient;
        _indexAliasResolver = indexAliasResolver;
        _searcherOptions = options.Value;
        _logger = logger;
    }
    
    public async Task<SearchResult> SearchAsync(
        string indexAlias,
        string? query = null,
        IEnumerable<Filter>? filters = null,
        IEnumerable<Facet>? facets = null,
        IEnumerable<Sorter>? sorters = null,
        string? culture = null,
        string? segment = null,
        AccessContext? accessContext = null,
        int skip = 0,
        int take = 10)
    {
        if (query is null && filters is null && facets is null && sorters is null)
        {
            return new SearchResult(0, [], []);
        }
        
        var searchOptions = new SearchOptions
        {
            Skip = skip,
            Size = take,
            IncludeTotalCount = true
        };
        
        // Build filter string
        var filterParts = new List<string>();
        
        // Culture filter - must always include invariant culture when searching for a specific culture
        var cultures = culture.IsNullOrWhiteSpace()
            ? new[] { IndexConstants.Variation.InvariantCulture }
            : new[] { culture.IndexCulture(), IndexConstants.Variation.InvariantCulture };
        var cultureFilter = string.Join(" or ", cultures.Select(c => $"{IndexConstants.FieldNames.Culture} eq '{c}'"));
        filterParts.Add($"({cultureFilter})");
        
        // Segment filter
        filterParts.Add($"{IndexConstants.FieldNames.Segment} eq '{segment.IndexSegment()}'");
        
        // Access filter - using any() for collection field
        Guid[] accessKeys = accessContext is null
            ? [Guid.Empty]
            : new[] { Guid.Empty, accessContext.PrincipalId }.Union(accessContext.GroupIds ?? []).ToArray();
        var accessFilter = string.Join(" or ", accessKeys.Select(k => $"{IndexConstants.FieldNames.AccessKeys}/any(x: x eq '{k:D}')"));
        filterParts.Add($"({accessFilter})");
        
        // Add filters from request
        Filter[] filtersArray = filters as Filter[] ?? filters?.ToArray() ?? [];
        foreach (var filter in filtersArray)
        {
            string? filterString = BuildFilterString(filter);
            if (!string.IsNullOrEmpty(filterString))
            {
                filterParts.Add(filterString);
            }
        }
        
        searchOptions.Filter = string.Join(" and ", filterParts);
        
        // Add facets
        Facet[] facetsArray = facets as Facet[] ?? facets?.ToArray() ?? [];
        foreach (var facet in facetsArray)
        {
            string? facetString = BuildFacetString(facet);
            if (!string.IsNullOrEmpty(facetString))
            {
                searchOptions.Facets.Add(facetString);
            }
        }
        
        // Add sorting
        Sorter[] sortersArray = sorters as Sorter[] ?? sorters?.ToArray() ?? [];
        if (sortersArray.Length > 0)
        {
            foreach (var sorter in sortersArray)
            {
                string? orderBy = BuildOrderByString(sorter);
                if (!string.IsNullOrEmpty(orderBy))
                {
                    searchOptions.OrderBy.Add(orderBy);
                }
            }
        }
        
        // Select only the fields we need
        searchOptions.Select.Add(IndexConstants.FieldNames.Key);
        searchOptions.Select.Add(IndexConstants.FieldNames.ObjectType);
        
        try
        {
            var resolvedIndexAlias = _indexAliasResolver.Resolve(indexAlias);
            SearchClient searchClient = _indexClient.GetSearchClient(resolvedIndexAlias);
            
            // Build search text with boost factors
            string searchText = query.IsNullOrWhiteSpace() ? "*" : query;
            
            // If we have a query, use weighted search fields
            if (!query.IsNullOrWhiteSpace())
            {
                searchOptions.SearchFields.Add(IndexConstants.FieldNames.AllTextsR1);
                searchOptions.SearchFields.Add(IndexConstants.FieldNames.AllTextsR2);
                searchOptions.SearchFields.Add(IndexConstants.FieldNames.AllTextsR3);
                searchOptions.SearchFields.Add(IndexConstants.FieldNames.AllTexts);
                
                // Note: Azure Search doesn't support per-field boosting in SearchFields
                // We'd need to use a scoring profile for that, which we can add as an enhancement
            }
            
            SearchResults<SearchDocument> response = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);
            
            // Extract documents
            var documents = new List<Document>();
            await foreach (var result in response.GetResultsAsync())
            {
                if (result.Document.TryGetValue(IndexConstants.FieldNames.Key, out object? keyValue) &&
                    Guid.TryParse(keyValue?.ToString(), out Guid key))
                {
                    UmbracoObjectTypes objectType = UmbracoObjectTypes.Unknown;
                    if (result.Document.TryGetValue(IndexConstants.FieldNames.ObjectType, out object? objectTypeValue) &&
                        Enum.TryParse(objectTypeValue?.ToString(), out UmbracoObjectTypes parsedType))
                    {
                        objectType = parsedType;
                    }
                    
                    documents.Add(new Document(key, objectType));
                }
            }
            
            // Extract facets with expand support
            var facetResults = new List<UmbracoFacetResult>();
            
            if (_searcherOptions.ExpandFacetValues && facetsArray.Length > 0)
            {
                // Get active facet filters (filters that have corresponding facets)
                var facetFieldNames = facetsArray.Select(f => f.FieldName).ToArray();
                var activeFacetFilters = filtersArray.Where(f => facetFieldNames.Contains(f.FieldName, StringComparer.InvariantCultureIgnoreCase)).ToArray();
                
                if (activeFacetFilters.Length > 0)
                {
                    // For each active facet, run a search without that specific filter to get all possible values
                    foreach (var activeFacet in facetsArray.Where(f => activeFacetFilters.Any(af => af.FieldName.Equals(f.FieldName, StringComparison.InvariantCultureIgnoreCase))))
                    {
                        // Build search options without this facet's filter
                        var expandedSearchOptions = new SearchOptions { Skip = 0, Size = 0, IncludeTotalCount = false };
                        
                        // Add facet for this field only
                        string? facetString = BuildFacetString(activeFacet);
                        if (!string.IsNullOrEmpty(facetString))
                        {
                            expandedSearchOptions.Facets.Add(facetString);
                        }
                        
                        // Build filter excluding this facet's filter
                        var expandedFilterParts = new List<string>(filterParts.Take(3)); // Culture, Segment, Access
                        foreach (var filter in filtersArray.Where(f => !f.FieldName.Equals(activeFacet.FieldName, StringComparison.InvariantCultureIgnoreCase)))
                        {
                            string? filterString = BuildFilterString(filter);
                            if (!string.IsNullOrEmpty(filterString))
                            {
                                expandedFilterParts.Add(filterString);
                            }
                        }
                        expandedSearchOptions.Filter = string.Join(" and ", expandedFilterParts);
                        
                        // Execute the expanded search
                        var expandedResponse = await searchClient.SearchAsync<SearchDocument>(searchText, expandedSearchOptions);
                        
                        // Extract facet results
                        if (expandedResponse.Value.Facets != null)
                        {
                            UmbracoFacetResult? expandedFacetResult = BuildFacetResult(activeFacet, expandedResponse.Value.Facets);
                            if (expandedFacetResult != null)
                            {
                                facetResults.Add(expandedFacetResult);
                            }
                        }
                    }
                    
                    // Add facets that don't have active filters (from main response)
                    var inactiveFacets = facetsArray.Except(facetsArray.Where(f => activeFacetFilters.Any(af => af.FieldName.Equals(f.FieldName, StringComparison.InvariantCultureIgnoreCase))));
                    if (response.Facets != null)
                    {
                        foreach (var facet in inactiveFacets)
                        {
                            UmbracoFacetResult? facetResult = BuildFacetResult(facet, response.Facets);
                            if (facetResult != null)
                            {
                                facetResults.Add(facetResult);
                            }
                        }
                    }
                }
                else
                {
                    // No active filters, use main response facets
                    if (response.Facets != null)
                    {
                        foreach (var facet in facetsArray)
                        {
                            UmbracoFacetResult? facetResult = BuildFacetResult(facet, response.Facets);
                            if (facetResult != null)
                            {
                                facetResults.Add(facetResult);
                            }
                        }
                    }
                }
            }
            else
            {
                // ExpandFacetValues disabled, use standard behavior
                if (response.Facets != null)
                {
                    foreach (var facet in facetsArray)
                    {
                        UmbracoFacetResult? facetResult = BuildFacetResult(facet, response.Facets);
                        if (facetResult != null)
                        {
                            facetResults.Add(facetResult);
                        }
                    }
                }
            }
            
            return new SearchResult(response.TotalCount ?? 0, documents, facetResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search in Azure Search");
            return new SearchResult(0, [], []);
        }
    }
    
    private string? BuildFilterString(Filter filter)
    {
        return filter switch
        {
            TextFilter textFilter => BuildTextFilter(textFilter),
            KeywordFilter keywordFilter => BuildKeywordFilter(keywordFilter),
            IntegerExactFilter integerExactFilter => BuildIntegerExactFilter(integerExactFilter),
            IntegerRangeFilter integerRangeFilter => BuildIntegerRangeFilter(integerRangeFilter),
            DecimalExactFilter decimalExactFilter => BuildDecimalExactFilter(decimalExactFilter),
            DecimalRangeFilter decimalRangeFilter => BuildDecimalRangeFilter(decimalRangeFilter),
            DateTimeOffsetExactFilter dateTimeOffsetExactFilter => BuildDateTimeOffsetExactFilter(dateTimeOffsetExactFilter),
            DateTimeOffsetRangeFilter dateTimeOffsetRangeFilter => BuildDateTimeOffsetRangeFilter(dateTimeOffsetRangeFilter),
            _ => null
        };
    }
    
    private string BuildTextFilter(TextFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.Texts);
        var conditions = filter.Values.Select(v => $"search.ismatch('{v.Replace("'", "''")}*', '{fieldName}')");
        var joined = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({joined})" : $"({joined})";
    }
    
    private string BuildKeywordFilter(KeywordFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.Keywords);
        var values = string.Join(",", filter.Values.Select(v => v.Replace("'", "''")));
        var condition = $"{fieldName}/any(x: search.in(x, '{values}'))";
        return filter.Negate ? $"not ({condition})" : condition;
    }
    
    private string BuildIntegerExactFilter(IntegerExactFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.Integers);
        var values = string.Join(",", filter.Values);
        var condition = $"{fieldName}/any(x: search.in(x, '{values}', ','))";
        return filter.Negate ? $"not ({condition})" : condition;
    }
    
    private string BuildIntegerRangeFilter(IntegerRangeFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.Integers);
        var conditions = filter.Ranges.Select(r =>
        {
            var min = r.MinValue ?? int.MinValue;
            var max = r.MaxValue ?? int.MaxValue;
            return $"{fieldName}/any(x: x ge {min} and x lt {max})";
        });
        var joined = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({joined})" : $"({joined})";
    }
    
    private string BuildDecimalExactFilter(DecimalExactFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.Decimals);
        var conditions = filter.Values.Select(v => $"{fieldName}/any(x: x eq {v})");
        var joined = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({joined})" : $"({joined})";
    }
    
    private string BuildDecimalRangeFilter(DecimalRangeFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.Decimals);
        var conditions = filter.Ranges.Select(r =>
        {
            var min = r.MinValue ?? decimal.MinValue;
            var max = r.MaxValue ?? decimal.MaxValue;
            return $"{fieldName}/any(x: x ge {min} and x lt {max})";
        });
        var joined = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({joined})" : $"({joined})";
    }
    
    private string BuildDateTimeOffsetExactFilter(DateTimeOffsetExactFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets);
        var conditions = filter.Values.Select(v => $"{fieldName}/any(x: x eq {v:O})");
        var joined = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({joined})" : $"({joined})";
    }
    
    private string BuildDateTimeOffsetRangeFilter(DateTimeOffsetRangeFilter filter)
    {
        var fieldName = FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets);
        var conditions = filter.Ranges.Select(r =>
        {
            var min = r.MinValue ?? DateTimeOffset.MinValue;
            var max = r.MaxValue ?? DateTimeOffset.MaxValue;
            return $"{fieldName}/any(x: x ge {min:O} and x lt {max:O})";
        });
        var joined = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({joined})" : $"({joined})";
    }
    
    private string? BuildFacetString(Facet facet)
    {
        return facet switch
        {
            KeywordFacet => $"{FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Keywords)},count:{_searcherOptions.MaxFacetValues}",
            IntegerExactFacet => $"{FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Integers)},count:{_searcherOptions.MaxFacetValues}",
            IntegerRangeFacet integerRangeFacet => BuildIntegerRangeFacetString(integerRangeFacet),
            DecimalExactFacet => $"{FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Decimals)},count:{_searcherOptions.MaxFacetValues}",
            DecimalRangeFacet decimalRangeFacet => BuildDecimalRangeFacetString(decimalRangeFacet),
            DateTimeOffsetExactFacet => $"{FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets)},count:{_searcherOptions.MaxFacetValues}",
            DateTimeOffsetRangeFacet dateTimeOffsetRangeFacet => BuildDateTimeOffsetRangeFacetString(dateTimeOffsetRangeFacet),
            _ => null
        };
    }
    
    private string? BuildIntegerRangeFacetString(IntegerRangeFacet facet)
    {
        // Note: Azure Search range facets use boundary values separated by |
        // For ranges like 1500-1600, 1600-1700, we extract unique boundaries: 1500|1600|1700
        var fieldName = FieldName(facet.FieldName, $"{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}");
        var boundaries = facet.Ranges
            .SelectMany(r => new[] { r.MinValue, r.MaxValue })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .OrderBy(v => v)
            .Select(v => v.ToString());
        return $"{fieldName},values:{string.Join("|", boundaries)}";
    }
    
    private string? BuildDecimalRangeFacetString(DecimalRangeFacet facet)
    {
        var fieldName = FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Decimals);
        var boundaries = facet.Ranges
            .SelectMany(r => new[] { r.MinValue, r.MaxValue })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .OrderBy(v => v)
            .Select(v => v.ToString());
        return $"{fieldName},values:{string.Join("|", boundaries)}";
    }
    
    private string? BuildDateTimeOffsetRangeFacetString(DateTimeOffsetRangeFacet facet)
    {
        var fieldName = FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets);
        var boundaries = facet.Ranges
            .SelectMany(r => new[] { r.MinValue, r.MaxValue })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .OrderBy(v => v)
            .Select(v => v.ToString("O"));
        return $"{fieldName},values:{string.Join("|", boundaries)}";
    }
    
    private string? BuildOrderByString(Sorter sorter)
    {
        var direction = sorter.Direction == Direction.Ascending ? "asc" : "desc";
        
        return sorter switch
        {
            ScoreSorter => $"search.score() {direction}",
            TextSorter => $"{FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.Texts)} {direction}",
            KeywordSorter => $"{FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.Keywords)} {direction}",
            IntegerSorter => $"{FieldName(sorter.FieldName, $"{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}")} {direction}",
            DecimalSorter => $"{FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.Decimals)} {direction}",
            DateTimeOffsetSorter => $"{FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets)} {direction}",
            _ => null
        };
    }
    
    private UmbracoFacetResult? BuildFacetResult(Facet facet, IDictionary<string, IList<AzureFacetResult>> azureFacets)
    {
        string fieldName = facet switch
        {
            KeywordFacet => FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Keywords),
            IntegerExactFacet => FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Integers),
            IntegerRangeFacet => FieldName(facet.FieldName, $"{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}"),
            DecimalExactFacet or DecimalRangeFacet => FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Decimals),
            DateTimeOffsetExactFacet or DateTimeOffsetRangeFacet => FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets),
            _ => string.Empty
        };
        
        if (string.IsNullOrEmpty(fieldName) || !azureFacets.TryGetValue(fieldName, out var azureFacetResults))
        {
            return null;
        }
        
        return facet switch
        {
            KeywordFacet => new UmbracoFacetResult(
                facet.FieldName,
                azureFacetResults.Select(f => new KeywordFacetValue(f.Value?.ToString() ?? string.Empty, f.Count ?? 0))
            ),
            IntegerExactFacet => new UmbracoFacetResult(
                facet.FieldName,
                azureFacetResults.Select(f => new IntegerExactFacetValue(int.Parse(f.Value?.ToString() ?? "0"), f.Count ?? 0))
            ),
            IntegerRangeFacet integerRangeFacet => BuildIntegerRangeFacetResult(integerRangeFacet, azureFacetResults),
            DecimalExactFacet => new UmbracoFacetResult(
                facet.FieldName,
                azureFacetResults.Select(f => new DecimalExactFacetValue(decimal.Parse(f.Value?.ToString() ?? "0"), f.Count ?? 0))
            ),
            DecimalRangeFacet decimalRangeFacet => BuildDecimalRangeFacetResult(decimalRangeFacet, azureFacetResults),
            DateTimeOffsetExactFacet => new UmbracoFacetResult(
                facet.FieldName,
                azureFacetResults.Select(f => new DateTimeOffsetExactFacetValue(DateTimeOffset.Parse(f.Value?.ToString() ?? DateTimeOffset.MinValue.ToString("O")), f.Count ?? 0))
            ),
            DateTimeOffsetRangeFacet dateTimeOffsetRangeFacet => BuildDateTimeOffsetRangeFacetResult(dateTimeOffsetRangeFacet, azureFacetResults),
            _ => null
        };
    }
    
    private UmbracoFacetResult BuildIntegerRangeFacetResult(IntegerRangeFacet facet, IList<AzureFacetResult> azureFacetResults)
    {
        // Azure Search returns range buckets between consecutive boundary values
        // We need to map them back to our defined ranges
        return new UmbracoFacetResult(
            facet.FieldName,
            facet.Ranges.Select(range =>
            {
                long count = 0;
                // Find Azure facet results that fall within this range
                foreach (var azureResult in azureFacetResults)
                {
                    if (azureResult.From != null && azureResult.To != null)
                    {
                        var fromValue = Convert.ToInt32(azureResult.From);
                        var toValue = Convert.ToInt32(azureResult.To);
                        
                        // Check if this Azure range overlaps with our defined range
                        if (fromValue >= (range.MinValue ?? int.MinValue) && toValue <= (range.MaxValue ?? int.MaxValue))
                        {
                            count += azureResult.Count ?? 0;
                        }
                    }
                }
                return new IntegerRangeFacetValue(range.Key, range.MinValue, range.MaxValue, count);
            })
        );
    }
    
    private UmbracoFacetResult BuildDecimalRangeFacetResult(DecimalRangeFacet facet, IList<AzureFacetResult> azureFacetResults)
    {
        return new UmbracoFacetResult(
            facet.FieldName,
            facet.Ranges.Select(range =>
            {
                long count = 0;
                foreach (var azureResult in azureFacetResults)
                {
                    if (azureResult.From != null && azureResult.To != null)
                    {
                        var fromValue = Convert.ToDecimal(azureResult.From);
                        var toValue = Convert.ToDecimal(azureResult.To);
                        
                        if (fromValue >= (range.MinValue ?? decimal.MinValue) && toValue <= (range.MaxValue ?? decimal.MaxValue))
                        {
                            count += azureResult.Count ?? 0;
                        }
                    }
                }
                return new DecimalRangeFacetValue(range.Key, range.MinValue, range.MaxValue, count);
            })
        );
    }
    
    private UmbracoFacetResult BuildDateTimeOffsetRangeFacetResult(DateTimeOffsetRangeFacet facet, IList<AzureFacetResult> azureFacetResults)
    {
        return new UmbracoFacetResult(
            facet.FieldName,
            facet.Ranges.Select(range =>
            {
                long count = 0;
                foreach (var azureResult in azureFacetResults)
                {
                    if (azureResult.From != null && azureResult.To != null)
                    {
                        var fromValue = DateTimeOffset.Parse(azureResult.From.ToString()!);
                        var toValue = DateTimeOffset.Parse(azureResult.To.ToString()!);
                        
                        if (fromValue >= (range.MinValue ?? DateTimeOffset.MinValue) && toValue <= (range.MaxValue ?? DateTimeOffset.MaxValue))
                        {
                            count += azureResult.Count ?? 0;
                        }
                    }
                }
                return new DateTimeOffsetRangeFacetValue(range.Key, range.MinValue, range.MaxValue, count);
            })
        );
    }
}

