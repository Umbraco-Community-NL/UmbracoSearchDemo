using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Kjac.SearchProvider.Elasticsearch.Constants;
using Kjac.SearchProvider.Elasticsearch.Extensions;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Search.Core.Extensions;
using Umbraco.Extensions;
using IndexField = Umbraco.Cms.Search.Core.Models.Indexing.IndexField;

namespace Kjac.SearchProvider.Elasticsearch.Services;

internal sealed class ElasticsearchIndexer : ElasticsearchIndexManagingServiceBase, IElasticsearchIndexer
{
    private readonly IElasticsearchClientFactory _clientFactory;
    private readonly IElasticsearchIndexManager _indexManager;
    private readonly IIndexAliasResolver _indexAliasResolver;
    private readonly ILogger<ElasticsearchIndexer> _logger;

    public ElasticsearchIndexer(
        IServerRoleAccessor serverRoleAccessor,
        IElasticsearchIndexManager indexManager,
        IElasticsearchClientFactory clientFactory,
        IIndexAliasResolver indexAliasResolver,
        ILogger<ElasticsearchIndexer> logger)
        : base(serverRoleAccessor)
    {
        _clientFactory = clientFactory;
        _indexManager = indexManager;
        _indexAliasResolver = indexAliasResolver;
        _logger = logger;
    }

    public async Task AddOrUpdateAsync(
        string indexAlias,
        Guid id,
        UmbracoObjectTypes objectType,
        IEnumerable<Variation> variations,
        IEnumerable<IndexField> fields,
        ContentProtection? protection)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }

        IEnumerable<IGrouping<string, IndexField>> fieldsByFieldName = fields.GroupBy(field => field.FieldName);
        IEnumerable<IndexDocument> documents = variations.Select(
            variation =>
            {
                // document variation
                var culture = variation.Culture.IndexCulture();
                var segment = variation.Segment.IndexSegment();

                // document access (no access maps to an empty key for querying)
                Guid[] accessKeys = protection?.AccessIds.Any() is true
                    ? protection.AccessIds.ToArray()
                    : [Guid.Empty];

                // relevant field values for this variation (including invariant fields)
                IndexField[] variationFields = fieldsByFieldName.Select(
                        g =>
                        {
                            IndexField[] applicableFields = g.Where(f =>
                                (variation.Culture is not null
                                 && variation.Segment is not null
                                 && f.Culture == variation.Culture
                                 && f.Segment == variation.Segment)
                                || (variation.Culture is not null
                                    && f.Culture == variation.Culture
                                    && f.Segment is null)
                                || (variation.Segment is not null
                                    && f.Culture is null
                                    && f.Segment == variation.Segment)
                                || (f.Culture is null && f.Segment is null)
                            ).ToArray();

                            return applicableFields.Any()
                                ? new IndexField(
                                    g.Key,
                                    new IndexValue
                                    {
                                        DateTimeOffsets = applicableFields.SelectMany(f => f.Value.DateTimeOffsets ?? []).NullIfEmpty(),
                                        Decimals = applicableFields.SelectMany(f => f.Value.Decimals ?? []).NullIfEmpty(),
                                        Integers = applicableFields.SelectMany(f => f.Value.Integers ?? []).NullIfEmpty(),
                                        Keywords = applicableFields.SelectMany(f => f.Value.Keywords ?? []).NullIfEmpty(),
                                        Texts = applicableFields.SelectMany(f => f.Value.Texts ?? []).NullIfEmpty(),
                                        TextsR1 = applicableFields.SelectMany(f => f.Value.TextsR1 ?? []).NullIfEmpty(),
                                        TextsR2 = applicableFields.SelectMany(f => f.Value.TextsR2 ?? []).NullIfEmpty(),
                                        TextsR3 = applicableFields.SelectMany(f => f.Value.TextsR3 ?? []).NullIfEmpty(),
                                    },
                                    variation.Culture,
                                    variation.Segment
                                )
                                : null;
                        }
                    )
                    .WhereNotNull()
                    .ToArray();

                // all text fields for "free text query on all fields"
                var allTexts = variationFields
                    .SelectMany(field => field.Value.Texts ?? [])
                    .ToArray();
                var allTextsR1 = variationFields
                    .SelectMany(field => field.Value.TextsR1 ?? [])
                    .ToArray();
                var allTextsR2 = variationFields
                    .SelectMany(field => field.Value.TextsR2 ?? [])
                    .ToArray();
                var allTextsR3 = variationFields
                    .SelectMany(field => field.Value.TextsR3 ?? [])
                    .ToArray();

                // explicit document field values
                var fieldValues = variationFields
                    .SelectMany(
                        field =>
                        {
                            return new (string FieldName, string Postfix, object[]? Values)[]
                            {
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.Texts,
                                    field.Value.Texts?.OfType<object>().ToArray()
                                ),
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.TextsR1,
                                    field.Value.TextsR1?.OfType<object>().ToArray()
                                ),
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.TextsR2,
                                    field.Value.TextsR2?.OfType<object>().ToArray()
                                ),
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.TextsR3,
                                    field.Value.TextsR3?.OfType<object>().ToArray()
                                ),
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.Integers,
                                    field.Value.Integers?.OfType<object>().ToArray()
                                ),
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.Decimals,
                                    field.Value.Decimals?.OfType<object>().ToArray()
                                ),
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.DateTimeOffsets,
                                    field.Value.DateTimeOffsets?.OfType<object>().ToArray()
                                ),
                                (
                                    field.FieldName,
                                    IndexConstants.FieldTypePostfix.Keywords,
                                    field.Value.Keywords?.OfType<object>().ToArray()
                                )
                            };
                        }
                    )
                    .Where(f => f.Values?.Any() is true)
                    .ToDictionary(f => $"{f.FieldName}{f.Postfix}", f => f.Values!);

                // add explicit fields for textual sorting across multiple relevance levels of text
                foreach (IndexField field in variationFields)
                {
                    var sortableTexts = (field.Value.TextsR1 ?? [])
                        .Union(field.Value.TextsR2 ?? [])
                        .Union(field.Value.TextsR3 ?? [])
                        .Union(field.Value.Texts ?? [])
                        .Take(5).ToArray();
                    if (sortableTexts.Length > 0)
                    {
                        fieldValues.Add(
                            $"{field.FieldName}{IndexConstants.FieldTypePostfix.Texts}{IndexConstants.FieldTypePostfix.Sortable}",
                            [string.Join(" ", sortableTexts).ToLowerInvariant()]
                        );
                    }
                }

                return new IndexDocument
                {
                    Id = $"{id:D}.{culture}.{segment}",
                    ObjectType = objectType.ToString(),
                    Key = id,
                    Culture = culture,
                    Segment = segment,
                    AccessKeys = accessKeys,
                    AllTexts = allTexts,
                    AllTextsR1 = allTextsR1,
                    AllTextsR2 = allTextsR2,
                    AllTextsR3 = allTextsR3,
                    Fields = fieldValues
                };
            }
        );

        ElasticsearchClient client = _clientFactory.GetClient();

        BulkResponse response = await client.IndexManyAsync(documents, index: _indexAliasResolver.Resolve(indexAlias));
        if (response.IsValidResponse is false)
        {
            LogFailedElasticResponse(_logger, indexAlias, "Could not perform add/update", response);
        }
    }

    public async Task DeleteAsync(string indexAlias, IEnumerable<Guid> ids)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }

        indexAlias = _indexAliasResolver.Resolve(indexAlias);

        ElasticsearchClient client = _clientFactory.GetClient();
        DeleteByQueryResponse result = await client.DeleteByQueryAsync<IndexDocument>(
            dr => dr
                .Indices(indexAlias)
                .Query(
                    qd => qd
                        .Terms(
                            td => td
                                .Field(
                                    FieldName(
                                        Umbraco.Cms.Search.Core.Constants.FieldNames.PathIds,
                                        IndexConstants.FieldTypePostfix.Keywords
                                    )
                                )
                                .Terms(
                                    new TermsQueryField(ids.Select(key => FieldValue.String(key.AsKeyword())).ToArray())
                                )
                        )
                )
        );

        if (result.IsValidResponse is false)
        {
            LogFailedElasticResponse(_logger, indexAlias, "Could not perform delete", result);
        }
    }

    public async Task ResetAsync(string indexAlias)
        => await _indexManager.ResetAsync(indexAlias);

    private record IndexDocument
    {
        [JsonPropertyName(IndexConstants.FieldNames.Id)]
        public required string Id { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.ObjectType)]
        public required string? ObjectType { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.Key)]
        public required Guid Key { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.Culture)]
        public required string Culture { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.Segment)]
        public required string Segment { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.AccessKeys)]
        public required Guid[] AccessKeys { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.AllTexts)]
        public required string[] AllTexts { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.AllTextsR1)]
        public required string[] AllTextsR1 { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.AllTextsR2)]
        public required string[] AllTextsR2 { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.AllTextsR3)]
        public required string[] AllTextsR3 { get; init; }

        [JsonPropertyName(IndexConstants.FieldNames.Fields)]
        public required Dictionary<string, object[]> Fields { get; init; }
    }
}
