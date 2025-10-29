using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Site.SearchProvider.Configuration;
using Site.SearchProvider.Constants;
using Umbraco.Cms.Core.Sync;
using CoreConstants = Umbraco.Cms.Search.Core.Constants;

namespace Site.SearchProvider.Services;

internal sealed class AzureSearchIndexManager : AzureSearchIndexManagingServiceBase, IAzureSearchIndexManager
{
    private readonly SearchIndexClient _indexClient;
    private readonly IIndexAliasResolver _indexAliasResolver;
    private readonly ILogger<AzureSearchIndexManager> _logger;
    
    public AzureSearchIndexManager(
        IServerRoleAccessor serverRoleAccessor,
        SearchIndexClient indexClient,
        IIndexAliasResolver indexAliasResolver,
        ILogger<AzureSearchIndexManager> logger)
        : base(serverRoleAccessor)
    {
        _indexClient = indexClient;
        _indexAliasResolver = indexAliasResolver;
        _logger = logger;
    }
    
    public async Task EnsureAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }
        
        indexAlias = _indexAliasResolver.Resolve(indexAlias);
        
        try
        {
            await _indexClient.GetIndexAsync(indexAlias);
            return; // Index already exists
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Index does not exist, create it
        }
        
        _logger.LogInformation("Creating Azure Search index {indexAlias}...", indexAlias);
        
        var index = new SearchIndex(indexAlias)
        {
            Fields =
            {
                // System fields - these are always present
                new SimpleField(IndexConstants.FieldNames.Id, SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.ObjectType, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.Key, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.Culture, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.Segment, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.AccessKeys, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
                
                // All texts fields for full-text search with boost
                new SearchableField(IndexConstants.FieldNames.AllTexts) { IsFilterable = false },
                new SearchableField(IndexConstants.FieldNames.AllTextsR1) { IsFilterable = false },
                new SearchableField(IndexConstants.FieldNames.AllTextsR2) { IsFilterable = false },
                new SearchableField(IndexConstants.FieldNames.AllTextsR3) { IsFilterable = false },
                
                // Common Umbraco fields - all text relevance levels for Name
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.Texts)) { IsSortable = true, IsFacetable = false },
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.TextsR1)) { IsSortable = false, IsFacetable = false },
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.TextsR2)) { IsSortable = false, IsFacetable = false },
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.TextsR3)) { IsSortable = false, IsFacetable = false },
                new SimpleField(FieldName(CoreConstants.FieldNames.ContentTypeId, IndexConstants.FieldTypePostfix.Keywords), SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = false },
                new SimpleField(FieldName(CoreConstants.FieldNames.PathIds, IndexConstants.FieldTypePostfix.Keywords), SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
                
                // Book-specific fields for the demo - all text relevance levels
                new SearchableField(FieldName("author", IndexConstants.FieldTypePostfix.Texts)) { IsFilterable = false, IsFacetable = false },
                new SearchableField(FieldName("author", IndexConstants.FieldTypePostfix.TextsR1)) { IsFilterable = false, IsFacetable = false },
                new SearchableField(FieldName("author", IndexConstants.FieldTypePostfix.TextsR2)) { IsFilterable = false, IsFacetable = false },
                new SearchableField(FieldName("author", IndexConstants.FieldTypePostfix.TextsR3)) { IsFilterable = false, IsFacetable = false },
                new SearchableField(FieldName("summary", IndexConstants.FieldTypePostfix.Texts)) { IsFilterable = false, IsFacetable = false },
                new SearchableField(FieldName("summary", IndexConstants.FieldTypePostfix.TextsR1)) { IsFilterable = false, IsFacetable = false },
                new SearchableField(FieldName("summary", IndexConstants.FieldTypePostfix.TextsR2)) { IsFilterable = false, IsFacetable = false },
                new SearchableField(FieldName("summary", IndexConstants.FieldTypePostfix.TextsR3)) { IsFilterable = false, IsFacetable = false },
                new SimpleField(FieldName("publishYear", IndexConstants.FieldTypePostfix.Integers), SearchFieldDataType.Collection(SearchFieldDataType.Int32)) { IsFilterable = true, IsFacetable = true, IsSortable = false },
                new SimpleField(FieldName("length", IndexConstants.FieldTypePostfix.Keywords), SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = true },
                new SimpleField(FieldName("authorNationality", IndexConstants.FieldTypePostfix.Keywords), SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = true },
                
                // Sortable fields for book data
                new SimpleField(FieldName("publishYear", $"{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}"), SearchFieldDataType.Int32) { IsSortable = true, IsFacetable = true },
            }
        };
        
        try
        {
            await _indexClient.CreateIndexAsync(index);
            _logger.LogInformation("Azure Search index {indexAlias} has been created.", indexAlias);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to create Azure Search index {indexAlias}", indexAlias);
            throw;
        }
    }
    
    public async Task ResetAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }
        
        indexAlias = _indexAliasResolver.Resolve(indexAlias);
        
        try
        {
            await _indexClient.DeleteIndexAsync(indexAlias);
            _logger.LogInformation("Deleted Azure Search index {indexAlias}", indexAlias);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Index doesn't exist, that's fine
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to delete Azure Search index {indexAlias}", indexAlias);
            return;
        }
        
        await EnsureAsync(indexAlias);
    }
}

