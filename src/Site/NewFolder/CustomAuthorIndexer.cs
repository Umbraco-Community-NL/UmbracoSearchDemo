using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Search.Core.Extensions;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.PropertyValueHandlers;
using IndexValue = Umbraco.Cms.Search.Core.Models.Indexing.IndexValue;

namespace Site.NewFolder
{
    public sealed class MultiNodeTreePickerPropertyValueHandler(
        IDataTypeConfigurationCache dataTypeConfigurationCache,
        IUmbracoContextFactory umbracoContextFactory)
        : IPropertyValueHandler
    {
        
        public bool CanHandle(string propertyEditorAlias)
            => propertyEditorAlias is Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.MultiNodeTreePicker;

        public IEnumerable<IndexField> GetIndexFields(IProperty property, string? culture, string? segment, bool published, IContentBase contentContext)
        {
            MultiNodePickerConfiguration? configuration = dataTypeConfigurationCache.GetConfigurationAs<MultiNodePickerConfiguration>(property.PropertyType.DataTypeKey);

            // NOTES:
            // - the default configuration for MNTP has ObjectType null, which is inferred as a document picker
            // - the DocumentObjectType is an internal constant in Umbraco 16 - value is "content"
            if (configuration?.TreeSource?.ObjectType is not (null or "content"))
            {
                return
                [
                    new IndexField("contentTypeAlias",
                        new IndexValue() { Keywords = [contentContext.ContentType.Alias] }, culture, segment)
                ];
            }

            if (property.Alias != "author")
            {
                return
                [
                    new IndexField("contentTypeAlias",
                        new IndexValue() { Keywords = [contentContext.ContentType.Alias] }, culture, segment)
                ];

            }


            var value = property.GetValue(culture, segment, published) as string;
            if (value.IsNullOrWhiteSpace())
            {
                return [];
            }

            var context = umbracoContextFactory.EnsureUmbracoContext().UmbracoContext;



            var udis = value
                .Split(Umbraco.Cms.Core.Constants.CharArrays.Comma, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => UdiParser.Parse(v));

            var keysAsKeywords = udis.Select(udi => context.Content.GetById(udi).Name).ToArray();

            return keysAsKeywords.Length > 0
                ? [new IndexField("authorName", new IndexValue { Keywords = keysAsKeywords }, culture, segment),
                new IndexField("contentTypeAlias", new IndexValue() { Keywords = [contentContext.ContentType.Alias] }, culture, segment)]
                : [];
        }
    }
}
