using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.PropertyValueHandlers;
using IndexValue = Umbraco.Cms.Search.Core.Models.Indexing.IndexValue;

namespace Site.ValueHandlers
{
    /// <summary>
    /// Provides property value handling for article-related properties using Multi-Node Tree Picker editors, enabling
    /// extraction and indexing of author and category names for search and filtering.
    /// </summary>
    /// <remarks>This handler specifically supports properties with the aliases "author" and "categories" that
    /// use the Multi-Node Tree Picker editor configured for content objects. It extracts the names of referenced
    /// content items for indexing purposes. Other property types or aliases are not handled by this
    /// implementation.</remarks>
    /// <param name="dataTypeConfigurationCache">The cache used to retrieve data type configurations for property editors.</param>
    /// <param name="umbracoContextFactory">The factory used to obtain Umbraco context instances for accessing content information.</param>
    public sealed class ArticlePropertyValueHandler(
        IDataTypeConfigurationCache dataTypeConfigurationCache,
        IUmbracoContextFactory umbracoContextFactory)
        : IPropertyValueHandler
    {
        /// <summary>
        /// Determines whether the specified property editor alias corresponds to a Multi-Node Tree Picker editor.
        /// </summary>
        /// <param name="propertyEditorAlias">The alias of the property editor to evaluate. Cannot be null.</param>
        /// <returns>true if the alias matches the Multi-Node Tree Picker editor; otherwise, false.</returns>
        public bool CanHandle(string propertyEditorAlias)
            => propertyEditorAlias is Constants.PropertyEditors.Aliases.MultiNodeTreePicker;

        /// <summary>
        /// Retrieves index fields for the specified property when the property represents either an author or a
        /// category, for use in search indexing.
        /// </summary>
        /// <param name="property">The property to extract index fields from. Must represent either an author or a category property.</param>
        /// <param name="culture">The culture code to use when retrieving the property's value, or <see langword="null"/> to use the default
        /// culture.</param>
        /// <param name="segment">The segment identifier to use when retrieving the property's value, or <see langword="null"/> if not
        /// segmented.</param>
        /// <param name="published">A value indicating whether to retrieve the published value of the property. Set to <see langword="true"/> to
        /// use the published value; otherwise, <see langword="false"/>.</param>
        /// <param name="contentContext">The content context used to resolve property values.</param>
        /// <returns>An enumerable collection of <see cref="IndexField"/> objects representing the index fields for the property.
        /// Returns an empty collection if the property is not an author or category, or if no value is available.</returns>
        public IEnumerable<IndexField> GetIndexFields(IProperty property, string? culture, string? segment, bool published, IContentBase contentContext)
        {
            var configuration = dataTypeConfigurationCache.GetConfigurationAs<MultiNodePickerConfiguration>(property.PropertyType.DataTypeKey);

            if (configuration?.TreeSource?.ObjectType is not (null or "content") || property.Alias != "author" && property.Alias != "categories")
            {
                return [];
            }

            var value = property.GetValue(culture, segment, published) as string;
            if (value.IsNullOrWhiteSpace())
            {
                return [];
            }
            
            var indexFields = new List<IndexField>();

            var field = property.Alias switch
            {
                "author" => GetIndexField("authorName", value, culture, segment),
                "categories" => GetIndexField("categoryName", value, culture, segment),
                _ => null
            };

            if (field != null)
            {
                indexFields.Add(field);
            }



            return indexFields;
        }

        /// <summary>
        /// Retrieves an index field containing keyword values derived from content items identified by the specified
        /// value string.
        /// </summary>
        /// <remarks>The method parses the value parameter into UDIs and retrieves the corresponding
        /// content item names as keywords. If no valid content items are found, the method returns null.</remarks>
        /// <param name="name">The name of the index field to create.</param>
        /// <param name="value">A comma-separated string of unique identifiers (UDIs) representing content items to include as keywords.</param>
        /// <param name="culture">An optional culture identifier to associate with the index field. If null, no culture is set.</param>
        /// <param name="segment">An optional segment identifier to associate with the index field. If null, no segment is set.</param>
        /// <returns>An IndexField instance containing the specified name and keywords if any valid content items are found;
        /// otherwise, null.</returns>
        private IndexField? GetIndexField(string name, string value, string? culture, string? segment)
        {
            var context = umbracoContextFactory.EnsureUmbracoContext().UmbracoContext;

            var udis = value
                .Split(Constants.CharArrays.Comma, StringSplitOptions.RemoveEmptyEntries)
                .Select(UdiParser.Parse);

            var keysAsKeywords = udis
                .Select(udi => context.Content.GetById(udi))
                .WhereNotNull()
                .Select(content => content.Name)
                .ToArray();

            return keysAsKeywords.Length == 0 ? null : new IndexField(name, new IndexValue { Keywords = keysAsKeywords }, culture, segment);
        }
    }
}
