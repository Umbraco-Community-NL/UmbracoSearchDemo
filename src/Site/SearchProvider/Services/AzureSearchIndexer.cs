using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Site.SearchProvider.Constants;
using Site.SearchProvider.Extensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Search.Core.Extensions;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Search.Core.Constants;

namespace Site.SearchProvider.Services;

internal sealed class AzureSearchIndexer : AzureSearchIndexManagingServiceBase, IAzureSearchIndexer
{
    private readonly SearchIndexClient _indexClient;
    private readonly IAzureSearchIndexManager _indexManager;
    private readonly IIndexAliasResolver _indexAliasResolver;
    private readonly ILogger<AzureSearchIndexer> _logger;
    
    public AzureSearchIndexer(
        IServerRoleAccessor serverRoleAccessor,
        SearchIndexClient indexClient,
        IAzureSearchIndexManager indexManager,
        IIndexAliasResolver indexAliasResolver,
        ILogger<AzureSearchIndexer> logger)
        : base(serverRoleAccessor)
    {
        _indexClient = indexClient;
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
        var documents = new List<SearchDocument>();
        
        foreach (var variation in variations)
        {
            // Document variation
            var culture = variation.Culture.IndexCulture();
            var segment = variation.Segment.IndexSegment();
            
            // Document access (no access maps to an empty key for querying)
            Guid[] accessKeys = protection?.AccessIds.Any() is true
                ? protection.AccessIds.ToArray()
                : [Guid.Empty];
            
            // Relevant field values for this variation (including invariant fields)
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
            
            // All text fields for "free text query on all fields"
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
            
            // Create the document
            // Note: Azure Search keys can only contain letters, digits, underscore (_), dash (-), or equal sign (=)
            // So we use underscores instead of dots for the composite key
            var document = new SearchDocument
            {
                [IndexConstants.FieldNames.Id] = $"{id:D}_{culture}_{segment}",
                [IndexConstants.FieldNames.ObjectType] = objectType.ToString(),
                [IndexConstants.FieldNames.Key] = id.ToString("D"),
                [IndexConstants.FieldNames.Culture] = culture,
                [IndexConstants.FieldNames.Segment] = segment,
                [IndexConstants.FieldNames.AccessKeys] = accessKeys.Select(k => k.ToString("D")).ToArray(),
                [IndexConstants.FieldNames.AllTexts] = string.Join(" ", allTexts),
                [IndexConstants.FieldNames.AllTextsR1] = string.Join(" ", allTextsR1),
                [IndexConstants.FieldNames.AllTextsR2] = string.Join(" ", allTextsR2),
                [IndexConstants.FieldNames.AllTextsR3] = string.Join(" ", allTextsR3)
            };
            
            // Add explicit field values only for known book fields
            // Note: Azure Search requires explicit schema, so we only index fields we defined in AzureSearchIndexManager
            var knownFields = new[] { "author", "publishYear", "length", "authorNationality", "summary", CoreConstants.FieldNames.Name };
            
            foreach (IndexField field in variationFields.Where(f => knownFields.Contains(f.FieldName)))
            {
                if (field.Value.Texts?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.Texts)] = string.Join(" ", field.Value.Texts);
                }
                
                if (field.Value.TextsR1?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.TextsR1)] = string.Join(" ", field.Value.TextsR1);
                }
                
                if (field.Value.TextsR2?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.TextsR2)] = string.Join(" ", field.Value.TextsR2);
                }
                
                if (field.Value.TextsR3?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.TextsR3)] = string.Join(" ", field.Value.TextsR3);
                }
                
                if (field.Value.Integers?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.Integers)] = field.Value.Integers.ToArray();
                    // Add sortable single value
                    document[FieldName(field.FieldName, $"{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}")] = field.Value.Integers.First();
                }
                
                if (field.Value.Decimals?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.Decimals)] = field.Value.Decimals.ToArray();
                }
                
                if (field.Value.DateTimeOffsets?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.DateTimeOffsets)] = field.Value.DateTimeOffsets.ToArray();
                }
                
                if (field.Value.Keywords?.Any() is true)
                {
                    document[FieldName(field.FieldName, IndexConstants.FieldTypePostfix.Keywords)] = field.Value.Keywords.ToArray();
                }
            }
            
            // Always include common Umbraco fields (ContentTypeId, PathIds)
            var contentTypeIdField = variationFields.FirstOrDefault(f => f.FieldName == CoreConstants.FieldNames.ContentTypeId);
            if (contentTypeIdField?.Value.Keywords?.Any() is true)
            {
                document[FieldName(CoreConstants.FieldNames.ContentTypeId, IndexConstants.FieldTypePostfix.Keywords)] = contentTypeIdField.Value.Keywords.ToArray();
            }
            
            var pathIdsField = variationFields.FirstOrDefault(f => f.FieldName == CoreConstants.FieldNames.PathIds);
            if (pathIdsField?.Value.Keywords?.Any() is true)
            {
                document[FieldName(CoreConstants.FieldNames.PathIds, IndexConstants.FieldTypePostfix.Keywords)] = pathIdsField.Value.Keywords.ToArray();
            }
            
            documents.Add(document);
        }
        
        try
        {
            var resolvedIndexAlias = _indexAliasResolver.Resolve(indexAlias);
            SearchClient searchClient = _indexClient.GetSearchClient(resolvedIndexAlias);
            
            IndexDocumentsResult result = await searchClient.MergeOrUploadDocumentsAsync(documents);
            
            if (result.Results.Any(r => !r.Succeeded))
            {
                var firstError = result.Results.FirstOrDefault(r => !r.Succeeded);
                _logger.LogWarning("Failed to index some documents. First error: {error}", firstError?.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing add/update to Azure Search");
            throw;
        }
    }
    
    public async Task DeleteAsync(string indexAlias, IEnumerable<Guid> ids)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }
        
        try
        {
            var resolvedIndexAlias = _indexAliasResolver.Resolve(indexAlias);
            SearchClient searchClient = _indexClient.GetSearchClient(resolvedIndexAlias);
            
            // Azure Search requires the key field value for deletion
            // We need to delete all variations of these documents
            var searchOptions = new SearchOptions
            {
                Filter = string.Join(" or ", ids.Select(id => $"{FieldName(CoreConstants.FieldNames.PathIds, IndexConstants.FieldTypePostfix.Keywords)}/any(x: x eq '{id.AsKeyword()}')")),
                Select = { IndexConstants.FieldNames.Id }
            };
            
            var searchResults = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
            var documentsToDelete = new List<SearchDocument>();
            
            await foreach (var result in searchResults.Value.GetResultsAsync())
            {
                documentsToDelete.Add(new SearchDocument
                {
                    [IndexConstants.FieldNames.Id] = result.Document[IndexConstants.FieldNames.Id]
                });
            }
            
            if (documentsToDelete.Any())
            {
                await searchClient.DeleteDocumentsAsync(documentsToDelete);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing delete from Azure Search");
            throw;
        }
    }
    
    public async Task ResetAsync(string indexAlias)
        => await _indexManager.ResetAsync(indexAlias);
}

