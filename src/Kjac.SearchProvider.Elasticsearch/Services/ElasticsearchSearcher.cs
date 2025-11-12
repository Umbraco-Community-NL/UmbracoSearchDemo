using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.Fluent;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Kjac.SearchProvider.Elasticsearch.Configuration;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Kjac.SearchProvider.Elasticsearch.Constants;
using Kjac.SearchProvider.Elasticsearch.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;

namespace Kjac.SearchProvider.Elasticsearch.Services;

internal sealed class ElasticsearchSearcher : ElasticsearchServiceBase, IElasticsearchSearcher
{
    private readonly IElasticsearchClientFactory _clientFactory;
    private readonly ILogger<ElasticsearchSearcher> _logger;
    private readonly IIndexAliasResolver _indexAliasResolver;
    private readonly SearcherOptions _searcherOptions;

    public ElasticsearchSearcher(
        IElasticsearchClientFactory clientFactory,
        IOptions<SearcherOptions> options,
        IIndexAliasResolver indexAliasResolver,
        ILogger<ElasticsearchSearcher> logger)
    {
        _clientFactory = clientFactory;
        _searcherOptions = options.Value;
        _indexAliasResolver = indexAliasResolver;
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

        ElasticsearchClient client = _clientFactory.GetClient();

        // add variance filters
        var indexCultures = culture is null
            ? new[] { IndexConstants.Variation.InvariantCulture }
            : new[] { culture.IndexCulture(), IndexConstants.Variation.InvariantCulture };
        var mustFilters = new List<Action<QueryDescriptor<SearchResultDocument>>>
        {
            qd => qd.Terms(
                td => td
                    .Field(IndexConstants.FieldNames.Culture)
                    .Terms(new TermsQueryField(indexCultures.Select(FieldValue.String).ToArray()))
            ),
            qd => qd.Term(
                td => td
                    .Field(IndexConstants.FieldNames.Segment)
                    .Value(segment.IndexSegment())
            )
        };

        // add protected access filter
        Guid[] accessKeys = accessContext is null
            ? [Guid.Empty]
            : new[] { Guid.Empty, accessContext.PrincipalId }.Union(accessContext.GroupIds ?? []).ToArray();
        mustFilters.Add(
            qd => qd.Terms(
                td => td
                    .Field(IndexConstants.FieldNames.AccessKeys)
                    .Terms(
                        new TermsQueryField(accessKeys.Select(key => FieldValue.String(key.ToString("D"))).ToArray())
                    )
            )
        );

        // add full text search filter
        if (query.IsNullOrWhiteSpace() is false)
        {
            mustFilters.Add(
                qd => qd
                    .Bool(
                        bd => bd
                            .Should(
                                MatchQuery(IndexConstants.FieldNames.AllTexts),
                                MatchQuery(IndexConstants.FieldNames.AllTextsR1, _searcherOptions.BoostFactorTextR1),
                                MatchQuery(IndexConstants.FieldNames.AllTextsR2, _searcherOptions.BoostFactorTextR2),
                                MatchQuery(IndexConstants.FieldNames.AllTextsR3, _searcherOptions.BoostFactorTextR3)
                            )
                    )
            );
        }

        // explicitly ignore duplicate facets
        Facet[] facetsAsArray = facets as Facet[] ?? facets?
                .GroupBy(FacetName)
                .Select(group => group.First())
                .ToArray()
            ?? [];
        // filters needs splitting into two parts; regular filters (not used for faceting) and facet filters
        // - regular filters must be applied before any facets are calculated (they narrow down the potential result set)
        // - facet filters must be applied after facets calculation has begun (additional considerations apply, see comments below)
        Filter[] filtersAsArray = filters as Filter[] ?? filters?.ToArray() ?? [];
        var facetFieldNames = facetsAsArray.Select(facet => facet.FieldName).ToArray();
        Filter[] facetFilters = filtersAsArray.Where(f => facetFieldNames.InvariantContains(f.FieldName)).ToArray();
        Filter[] regularFilters = filtersAsArray.Except(facetFilters).ToArray();

        // add regular filters
        mustFilters.AddRange(regularFilters.Where(filter => filter.Negate is false).Select(FilterDescriptor));
        Action<QueryDescriptor<SearchResultDocument>>[] mustNotFilters = regularFilters
            .Where(filter => filter.Negate)
            .Select(FilterDescriptor)
            .ToArray();

        // add post filters for facets
        Action<QueryDescriptor<SearchResultDocument>>[] mustPostFilters = facetFilters
            .Where(filter => filter.Negate is false)
            .Select(FilterDescriptor)
            .ToArray();
        Action<QueryDescriptor<SearchResultDocument>>[] mustNotPostFilters = facetFilters
            .Where(filter => filter.Negate)
            .Select(FilterDescriptor)
            .ToArray();

        SearchResponse<SearchResultDocument> result = await client.SearchAsync<SearchResultDocument>(
            sr => sr
                .Indices(_indexAliasResolver.Resolve(indexAlias))
                .From(skip)
                .Size(take)
                .Query(
                    qd => qd
                        .Bool(
                            bd => bd
                                .Must(mustFilters.ToArray())
                                .MustNot(mustNotFilters)
                        )
                )
                .Aggregations(
                    a =>
                    {
                        foreach (Facet facet in facetsAsArray)
                        {
                            AddAggregationDescriptor(a, facet, facetFilters);
                        }
                    }
                )
                .PostFilter(
                    qd => qd
                        .Bool(
                            bd => bd
                                .Must(mustPostFilters)
                                .MustNot(mustNotPostFilters)
                        )
                )
                .Sort((sorters ?? [new ScoreSorter(Direction.Descending)]).SelectMany(SortOptionsDescriptors).ToArray())
                .Source(new SourceConfig(false))
                .Fields(
                    new FieldAndFormat { Field = IndexConstants.FieldNames.Key },
                    new FieldAndFormat { Field = IndexConstants.FieldNames.ObjectType }
                )
        );

        if (result.IsValidResponse is false)
        {
            LogFailedElasticResponse(_logger, indexAlias, "Could not execute a search", result);
            return new SearchResult(0, [], []);
        }

        Document[] keys = ExtractDocuments(result);
        IEnumerable<FacetResult> facetResult = ExtractFacetResult(facetsAsArray, result.Aggregations);

        return new SearchResult(result.Total, keys, facetResult);

        // full text filter uses boolean prefix (AND)
        Action<QueryDescriptor<SearchResultDocument>> MatchQuery(string fieldName, float boost = 1.0f) =>
            sd => sd.MatchBoolPrefix(
                mq => mq
                    .Field(fieldName)
                    .Query(query)
                    .Operator(Operator.And)
                    .Boost(boost)
            );
    }

    private void AddAggregationDescriptor(
        FluentDictionaryOfStringAggregation<SearchResultDocument> aggs,
        Facet facet,
        Filter[] facetFilters)
    {
        facetFilters = facetFilters.Where(f => f.FieldName != facet.FieldName).ToArray();
        if (facetFilters.Length is 0)
        {
            Inner(aggs, facet);
            return;
        }

        Action<QueryDescriptor<SearchResultDocument>>[] facetMustFilters = facetFilters
            .Where(filter => filter.Negate is false)
            .Select(FilterDescriptor)
            .ToArray();
        Action<QueryDescriptor<SearchResultDocument>>[] facetMustNotFilters = facetFilters
            .Where(filter => filter.Negate)
            .Select(FilterDescriptor)
            .ToArray();

        aggs.Add(
            FacetName(facet),
            ad => ad
                .Filter(qd => qd.Bool(bd => bd.Must(facetMustFilters).MustNot(facetMustNotFilters)))
                .Aggregations(
                    a =>
                    {
                        Inner(a, facet);
                    }
                )
        );

        return;

        void Inner(FluentDictionaryOfStringAggregation<SearchResultDocument> aggsInner, Facet facetInner)
        {
            try
            {
                switch (facetInner)
                {
                    case KeywordFacet:
                    case IntegerExactFacet:
                    case DecimalExactFacet:
                    case DateTimeOffsetExactFacet:
                        aggsInner.Add(
                            FacetName(facetInner),
                            ad => ad.Terms(td => td.Field(FieldName(facetInner)).Size(_searcherOptions.MaxFacetValues))
                        );
                        break;
                    case IntegerRangeFacet integerRangeFacet:
                        if (integerRangeFacet.Ranges.Length == 0)
                        {
                            _logger.LogWarning(
                                "The range facet for field \"{FieldName}\" had no ranges defined, so it was skipped.",
                                integerRangeFacet.FieldName
                            );
                            break;
                        }

                        aggsInner.Add(
                            FacetName(facetInner),
                            ad => ad
                                .Range(
                                    rd => rd
                                        .Field(FieldName(facetInner))
                                        .Ranges(
                                            integerRangeFacet.Ranges
                                                .Select(
                                                    r =>
                                                        new AggregationRange { Key = r.Key, From = r.MinValue, To = r.MaxValue, }
                                                )
                                                .ToArray()
                                        )
                                )
                        );
                        break;
                    case DecimalRangeFacet decimalRangeFacet:
                        if (decimalRangeFacet.Ranges.Length == 0)
                        {
                            _logger.LogWarning(
                                "The range facet for field \"{FieldName}\" had no ranges defined, so it was skipped.",
                                decimalRangeFacet.FieldName
                            );
                            break;
                        }

                        aggsInner.Add(
                            FacetName(facetInner),
                            ad => ad
                                .Range(
                                    rd => rd
                                        .Field(FieldName(facetInner))
                                        .Ranges(
                                            decimalRangeFacet.Ranges
                                                .Select(
                                                    r =>
                                                        new AggregationRange
                                                        {
                                                            Key = r.Key,
                                                            From = r.MinValue.HasValue
                                                                ? Convert.ToDouble(r.MinValue.Value)
                                                                : null,
                                                            To = r.MaxValue.HasValue
                                                                ? Convert.ToDouble(r.MaxValue.Value)
                                                                : null
                                                        }
                                                )
                                                .ToArray()
                                        )
                                )
                        );
                        break;
                    case DateTimeOffsetRangeFacet dateTimeOffsetRangeFacet:
                        if (dateTimeOffsetRangeFacet.Ranges.Length == 0)
                        {
                            _logger.LogWarning(
                                "The range facet for field \"{FieldName}\" had no ranges defined, so it was skipped.",
                                dateTimeOffsetRangeFacet.FieldName
                            );
                            break;
                        }

                        aggsInner.Add(
                            FacetName(facetInner),
                            ad => ad
                                .Range(
                                    rd => rd
                                        .Field(FieldName(facetInner))
                                        .Ranges(
                                            dateTimeOffsetRangeFacet.Ranges
                                                .Select(
                                                    r =>
                                                        new AggregationRange
                                                        {
                                                            Key = r.Key,
                                                            From = r.MinValue?.ToUnixTimeMilliseconds(),
                                                            To = r.MaxValue?.ToUnixTimeMilliseconds()
                                                        }
                                                )
                                                .ToArray()
                                        )
                                )
                        );
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(facetInner),
                            $"Encountered an unsupported facet type: {facetInner.GetType().Name}"
                        );
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(
                    ex,
                    "An error occurred while attempting to add a facet for field \"{FieldName}\", so it was skipped.",
                    facetInner.FieldName
                );
            }
        }
    }

    private Document[] ExtractDocuments(SearchResponse<SearchResultDocument> searchResult)
        => searchResult.Hits.Select(
                hit =>
                {
                    // hit field values are returned as JsonElement values
                    if (hit.Fields?.TryGetValue(IndexConstants.FieldNames.Key, out var keyValue) is true
                        && keyValue is JsonElement { ValueKind: JsonValueKind.Array } keyJsonValue
                        && Guid.TryParse(keyJsonValue.EnumerateArray().FirstOrDefault().GetString(), out Guid key))
                    {
                        UmbracoObjectTypes objectType =
                            hit.Fields?.TryGetValue(
                                IndexConstants.FieldNames.ObjectType,
                                out var objectTypeValue
                            ) is true
                            && objectTypeValue is JsonElement { ValueKind: JsonValueKind.Array } objectTypeJsonValue
                            && Enum.TryParse(
                                objectTypeJsonValue.EnumerateArray().FirstOrDefault().GetString(),
                                out UmbracoObjectTypes parserObjectType
                            )
                                ? parserObjectType
                                : UmbracoObjectTypes.Unknown;

                        return new Document(key, objectType);
                    }

                    _logger.LogWarning(
                        "Required document fields were not found in Elasticsearch result hit: {hitId}",
                        hit.Id
                    );
                    return null;
                }
            )
            .WhereNotNull()
            .ToArray();

    private IEnumerable<FacetResult> ExtractFacetResult(Facet[] facets, AggregateDictionary? aggregations)
    {
        if (aggregations is not null)
        {
            return facets
                .Select(facet => ExtractSingleAggregateFacetResult(facet, aggregations))
                .WhereNotNull()
                .ToArray();
        }

        if (facets.Length is not 0)
        {
            _logger.LogWarning("Expected {facetCount} facet aggregations from Elastic, but got none.", facets.Length);
        }

        return [];
    }

    private FacetResult? ExtractSingleAggregateFacetResult(Facet facet, AggregateDictionary aggregations)
    {
        if (aggregations.TryGetValue(FacetName(facet), out IAggregate? aggregation) is false)
        {
            _logger.LogWarning(
                "Could not find any facet aggregation for facet: {facetName}. Facet results might be incorrect.",
                facet.FieldName
            );
            return null;
        }

        if (aggregation is FilterAggregate filterAggregate)
        {
            if (filterAggregate.Aggregations?.TryGetValue(FacetName(facet), out aggregation) is not true)
            {
                _logger.LogWarning(
                    "Could not find the expected, nested facet aggregation for facet: {facetName}. Facet results might be incorrect.",
                    facet.FieldName
                );
                return null;
            }
        }

        FacetResult? facetResult = facet switch
        {
            KeywordFacet keywordFacet when aggregation is StringTermsAggregate stringTermsAggregate
                => new FacetResult(
                    keywordFacet.FieldName,
                    stringTermsAggregate.Buckets.Select(
                        bucket => new KeywordFacetValue(
                            bucket.Key.TryGetString(out var key) ? key : "-",
                            bucket.DocCount
                        )
                    )
                ),
            IntegerExactFacet integerExactFacet when aggregation is LongTermsAggregate longTermsAggregate
                => new FacetResult(
                    integerExactFacet.FieldName,
                    longTermsAggregate.Buckets.Select(
                        bucket => new IntegerExactFacetValue(Convert.ToInt32(bucket.Key), bucket.DocCount)
                    )
                ),
            IntegerRangeFacet integerRangeFacet when aggregation is RangeAggregate rangeAggregate
                => new FacetResult(
                    integerRangeFacet.FieldName,
                    rangeAggregate.Buckets.Select(
                        bucket => new IntegerRangeFacetValue(
                            bucket.Key ?? "n/a",
                            bucket.From.HasValue ? Convert.ToInt32(bucket.From.Value) : null,
                            bucket.To.HasValue ? Convert.ToInt32(bucket.To.Value) : null,
                            bucket.DocCount
                        )
                    )
                ),
            DecimalExactFacet decimalExactFacet when aggregation is DoubleTermsAggregate doubleTermsAggregate
                => new FacetResult(
                    decimalExactFacet.FieldName,
                    doubleTermsAggregate.Buckets.Select(
                        bucket => new DecimalExactFacetValue(Convert.ToDecimal(bucket.Key), bucket.DocCount)
                    )
                ),
            DecimalRangeFacet decimalRangeFacet when aggregation is RangeAggregate rangeAggregate
                => new FacetResult(
                    decimalRangeFacet.FieldName,
                    rangeAggregate.Buckets.Select(
                        bucket => new DecimalRangeFacetValue(
                            bucket.Key ?? "n/a",
                            bucket.From.HasValue ? Convert.ToDecimal(bucket.From.Value) : null,
                            bucket.To.HasValue ? Convert.ToDecimal(bucket.To.Value) : null,
                            bucket.DocCount
                        )
                    )
                ),
            DateTimeOffsetExactFacet dateTimeOffsetExactFacet when aggregation is LongTermsAggregate longTermsAggregate
                => new FacetResult(
                    dateTimeOffsetExactFacet.FieldName,
                    longTermsAggregate.Buckets.Select(
                        bucket => new DateTimeOffsetExactFacetValue(
                            DateTimeOffset.FromUnixTimeMilliseconds(bucket.Key),
                            bucket.DocCount
                        )
                    )
                ),
            DateTimeOffsetRangeFacet dateTimeOffsetRangeFacet when aggregation is RangeAggregate rangeAggregate
                => new FacetResult(
                    dateTimeOffsetRangeFacet.FieldName,
                    rangeAggregate.Buckets.Select(
                        bucket => new DateTimeOffsetRangeFacetValue(
                            bucket.Key ?? "n/a",
                            bucket.From.HasValue
                                ? DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(bucket.From.Value))
                                : null,
                            bucket.To.HasValue
                                ? DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(bucket.To.Value))
                                : null,
                            bucket.DocCount
                        )
                    )
                ),
            // this happens when querying for a facet that does not exist in the result set
            _ when aggregation is StringTermsAggregate { Buckets.Count: 0 } => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(facet),
                $"Encountered an unsupported facet type: {facet.GetType().Name}"
            )
        };

        if (facetResult is null)
        {
            _logger.LogWarning(
                "Unable to extract facet results for facet: {facetName}. Possible mismatch between the requested facet type and the indexed facet value. Facet results might be incorrect.",
                facet.FieldName
            );
        }

        return facetResult;
    }

    private static string FieldName(Filter filter)
        => filter switch
        {
            DateTimeOffsetExactFilter or DateTimeOffsetRangeFilter => FieldName(
                filter.FieldName,
                IndexConstants.FieldTypePostfix.DateTimeOffsets
            ),
            DecimalExactFilter or DecimalRangeFilter => FieldName(
                filter.FieldName,
                IndexConstants.FieldTypePostfix.Decimals
            ),
            IntegerExactFilter or IntegerRangeFilter => FieldName(
                filter.FieldName,
                IndexConstants.FieldTypePostfix.Integers
            ),
            KeywordFilter => FieldName(filter.FieldName, IndexConstants.FieldTypePostfix.Keywords),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filter),
                $"Encountered an unsupported filter type: {filter.GetType().Name}"
            )
        };

    private static string FieldName(Facet facet)
        => facet switch
        {
            IntegerExactFacet or IntegerRangeFacet => FieldName(
                facet.FieldName,
                IndexConstants.FieldTypePostfix.Integers
            ),
            KeywordFacet => FieldName(facet.FieldName, IndexConstants.FieldTypePostfix.Keywords),
            DecimalExactFacet or DecimalRangeFacet => FieldName(
                facet.FieldName,
                IndexConstants.FieldTypePostfix.Decimals
            ),
            DateTimeOffsetExactFacet or DateTimeOffsetRangeFacet => FieldName(
                facet.FieldName,
                IndexConstants.FieldTypePostfix.DateTimeOffsets
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(facet),
                $"Encountered an unsupported facet type: {facet.GetType().Name}"
            )
        };

    private static string FieldName(Sorter sorter)
        => sorter switch
        {
            DateTimeOffsetSorter => FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets),
            DecimalSorter => FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.Decimals),
            IntegerSorter => FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.Integers),
            KeywordSorter => FieldName(sorter.FieldName, IndexConstants.FieldTypePostfix.Keywords),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sorter),
                $"Encountered an unsupported sorter type: {sorter.GetType().Name}"
            )
        };

    private static string FacetName(Facet facet)
        => $"{facet.FieldName}_{facet.GetType().Name}";

    private Action<QueryDescriptor<SearchResultDocument>> FilterDescriptor(Filter filter)
        => filter switch
        {
            TextFilter textFilter => qd => qd
                .Bool(
                    bd => bd.Should(WildcardFilterQueryDescriptors(textFilter))
                ),
            KeywordFilter keywordFilter => qd => qd
                .Terms(
                    td => td
                        .Field(FieldName(filter))
                        .Terms(new TermsQueryField(keywordFilter.Values.Select(FieldValue.String).ToArray()))
                ),
            IntegerExactFilter integerExactFilter => qd => qd
                .Terms(
                    td => td
                        .Field(FieldName(filter))
                        .Terms(new TermsQueryField(integerExactFilter.Values.Select(v => FieldValue.Long(v)).ToArray()))
                ),
            IntegerRangeFilter integerRangeFilter => qd => qd
                .Bool(
                    bd => bd
                        .Should(
                            integerRangeFilter
                                .Ranges
                                .Select(r => IntegerRangeFilterQueryDescriptor(FieldName(filter), r))
                                .ToArray()
                        )
                ),
            DecimalExactFilter decimalExactFilter => qd => qd
                .Terms(
                    td => td
                        .Field(FieldName(filter))
                        .Terms(
                            new TermsQueryField(
                                decimalExactFilter.Values
                                    .Select(v => FieldValue.Double(Convert.ToDouble(v))).ToArray()
                            )
                        )
                ),
            DecimalRangeFilter decimalRangeFilter => qd => qd
                .Bool(
                    bd => bd
                        .Should(
                            decimalRangeFilter
                                .Ranges
                                .Select(r => DecimalRangeFilterQueryDescriptor(FieldName(filter), r))
                                .ToArray()
                        )
                ),
            DateTimeOffsetExactFilter dateTimeOffsetExactFilter => qd => qd
                .Terms(
                    td => td
                        .Field(FieldName(filter))
                        .Terms(
                            new TermsQueryField(
                                dateTimeOffsetExactFilter.Values
                                    .Select(v => FieldValue.String(v.ToString("O"))).ToArray()
                            )
                        )
                ),
            DateTimeOffsetRangeFilter dateTimeOffsetRangeFilter => qd => qd
                .Bool(
                    bd => bd
                        .Should(
                            dateTimeOffsetRangeFilter
                                .Ranges
                                .Select(r => DateTimeOffsetRangeFilterQueryDescriptor(FieldName(filter), r))
                                .ToArray()
                        )
                ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filter),
                $"Encountered an unsupported filter type: {filter.GetType().Name}"
            )
        };

    private Action<QueryDescriptor<SearchResultDocument>>[] WildcardFilterQueryDescriptors(TextFilter textFilter)
    {
        var fieldNameTexts = FieldName(textFilter.FieldName, IndexConstants.FieldTypePostfix.Texts);
        var fieldNameTextsR1 = FieldName(textFilter.FieldName, IndexConstants.FieldTypePostfix.TextsR1);
        var fieldNameTextsR2 = FieldName(textFilter.FieldName, IndexConstants.FieldTypePostfix.TextsR2);
        var fieldNameTextsR3 = FieldName(textFilter.FieldName, IndexConstants.FieldTypePostfix.TextsR3);
        return textFilter.Values.Select(text => WildcardFilterQueryDescriptor(fieldNameTexts, text))
            .Union(
                textFilter.Values.Select(
                    text => WildcardFilterQueryDescriptor(fieldNameTextsR1, text, _searcherOptions.BoostFactorTextR1)
                )
            )
            .Union(
                textFilter.Values.Select(
                    text => WildcardFilterQueryDescriptor(fieldNameTextsR2, text, _searcherOptions.BoostFactorTextR2)
                )
            )
            .Union(
                textFilter.Values.Select(
                    text => WildcardFilterQueryDescriptor(fieldNameTextsR3, text, _searcherOptions.BoostFactorTextR3)
                )
            )
            .ToArray();
    }

    private Action<QueryDescriptor<SearchResultDocument>> WildcardFilterQueryDescriptor(
        string fieldName,
        string text,
        float boost = 1.0f)
        => ad => ad.Wildcard(
            wd => wd
                .Field(fieldName)
                .Value($"{text.Replace("*", string.Empty)}*")
                .Boost(boost)
        );

    private Action<QueryDescriptor<SearchResultDocument>> IntegerRangeFilterQueryDescriptor(
        string fieldName,
        IntegerRangeFilterRange filterRange)
        => ad => ad.Range(
            rd => rd
                .Number(
                    nr => nr
                        .Field(fieldName)
                        .Gte(filterRange.MinValue ?? int.MinValue)
                        .Lt(filterRange.MaxValue ?? int.MaxValue)
                )
        );

    private Action<QueryDescriptor<SearchResultDocument>> DecimalRangeFilterQueryDescriptor(
        string fieldName,
        DecimalRangeFilterRange filterRange)
        => ad => ad.Range(
            rd => rd
                .Number(
                    nr => nr
                        .Field(fieldName)
                        .Gte(
                            filterRange.MinValue.HasValue
                                ? Convert.ToDouble(filterRange.MinValue.Value)
                                : double.MinValue
                        )
                        .Lt(
                            filterRange.MaxValue.HasValue
                                ? Convert.ToDouble(filterRange.MaxValue.Value)
                                : double.MaxValue
                        )
                )
        );

    private Action<QueryDescriptor<SearchResultDocument>> DateTimeOffsetRangeFilterQueryDescriptor(
        string fieldName,
        DateTimeOffsetRangeFilterRange filterRange)
        => ad => ad.Range(
            rd => rd
                .Date(
                    nr => nr
                        .Field(fieldName)
                        .Gte((filterRange.MinValue ?? DateTimeOffset.MinValue).DateTime)
                        .Lt((filterRange.MaxValue ?? DateTimeOffset.MaxValue).DateTime)
                )
        );

    private IEnumerable<Action<SortOptionsDescriptor<SearchResultDocument>>> SortOptionsDescriptors(Sorter sorter)
    {
        SortOrder sortOrder = sorter.Direction is Direction.Ascending ? SortOrder.Asc : SortOrder.Desc;
        return sorter switch
        {
            ScoreSorter =>
            [
                sd => sd.Score(new ScoreSort { Order = sortOrder })
            ],
            TextSorter =>
            [
                sd => sd
                    .Field(
                        // NOTE: we're utilizing that dynamically mapped text fields have a .keyword subfield
                        FieldName(sorter.FieldName, $"{IndexConstants.FieldTypePostfix.Texts}{IndexConstants.FieldTypePostfix.Sortable}.keyword"),
                        fd => fd.Order(sortOrder)
                    )
            ],
            _ =>
            [
                sd => sd
                    .Field(
                        FieldName(sorter),
                        fd => fd
                            .Order(sortOrder)
                            .NumericType(
                                sorter switch
                                {
                                    KeywordSorter => null,
                                    IntegerSorter => FieldSortNumericType.Long,
                                    DecimalSorter => FieldSortNumericType.Double,
                                    DateTimeOffsetSorter => FieldSortNumericType.Date,
                                    _ => throw new ArgumentOutOfRangeException(
                                        nameof(sorter),
                                        $"Encountered an unsupported sorter type: {sorter.GetType().Name}"
                                    )
                                }
                            )
                    )
            ]
        };
    }

    // NOTE: the Elasticsearch client is strongly typed, but since we don't care about indexed data, we won't be returning any;
    //       we'll use explicit fields extraction to get the document keys from a search result.
    private record SearchResultDocument
    {
    }
}
