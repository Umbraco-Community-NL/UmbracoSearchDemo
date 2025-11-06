using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.PropertyValueHandlers;
using IndexValue = Umbraco.Cms.Search.Core.Models.Indexing.IndexValue;

namespace Site.ValueHandlers
{
    public sealed class ArticleDatePropertyValueHandler : IPropertyValueHandler
    {

        public bool CanHandle(string propertyEditorAlias)
            => propertyEditorAlias is Constants.PropertyEditors.Aliases.DateTime
                or Constants.PropertyEditors.Aliases.PlainDateTime;

        public IEnumerable<IndexField> GetIndexFields(IProperty property, string? culture, string? segment,
            bool published, IContentBase contentContext)
        {

            if (property.Alias != "articleDate")
            {
                return [];
            }


            if (property.GetValue(culture, segment, published) is DateTime dateTime)
            {
                return
                [
                    new IndexField("articleYear", new IndexValue { Integers = [dateTime.Year] },
                        culture, segment)];
            }

            return [];
        }

    }
}
