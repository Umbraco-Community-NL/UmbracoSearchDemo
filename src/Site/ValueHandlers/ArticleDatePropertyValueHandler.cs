using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.PropertyValueHandlers;
using IndexValue = Umbraco.Cms.Search.Core.Models.Indexing.IndexValue;

namespace Site.ValueHandlers
{
    /// <summary>
    /// Provides handling for the 'articleDate' property value, enabling extraction and indexing of date-related fields
    /// for articles.
    /// </summary>
    /// <remarks>This handler supports property editors with the aliases 'DateTime' and 'PlainDateTime'. It is
    /// typically used in indexing scenarios to generate fields such as the article's date and year for search or
    /// filtering purposes. The handler only processes properties with the alias 'articleDate'; other properties are
    /// ignored.</remarks>
    public sealed class ArticleDatePropertyValueHandler : IPropertyValueHandler
    {
        /// <summary>
        /// Determines whether the specified property editor alias is supported by this handler.
        /// </summary>
        /// <remarks>Supported aliases include those for date-time and plain date-time property editors.
        /// This method does not perform validation on the format of the alias beyond matching known supported
        /// values.</remarks>
        /// <param name="propertyEditorAlias">The alias of the property editor to check for compatibility. Cannot be null.</param>
        /// <returns>true if the property editor alias matches a supported date-time editor; otherwise, false.</returns>
        public bool CanHandle(string propertyEditorAlias)
            => propertyEditorAlias is Constants.PropertyEditors.Aliases.DateTime
                or Constants.PropertyEditors.Aliases.PlainDateTime;

        /// <summary>
        /// Returns index fields for the specified property if its alias is "articleDate" and its value is a valid date.
        /// </summary>
        /// <remarks>The returned index fields include the original date and the year extracted from the
        /// date value. This method only processes properties with the alias "articleDate"; all other properties result
        /// in no index fields.</remarks>
        /// <param name="property">The property to evaluate for index field generation. Must not be null.</param>
        /// <param name="culture">The culture code to use when retrieving the property's value. Can be null to use the default culture.</param>
        /// <param name="segment">The segment identifier to use when retrieving the property's value. Can be null if segmentation is not
        /// required.</param>
        /// <param name="published">A value indicating whether to retrieve the published value of the property. If <see langword="true"/>, the
        /// published value is used; otherwise, the draft value is used.</param>
        /// <param name="contentContext">The content context associated with the property. Must not be null.</param>
        /// <returns>An enumerable collection of <see cref="IndexField"/> objects representing the index fields for the property.
        /// Returns an empty collection if the property alias is not "articleDate" or if the value is not a valid date.</returns>
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
                    new IndexField(property.Alias, new IndexValue()
                    {
                        DateTimeOffsets = [dateTime.Date]
                    }, culture, segment),

                    new IndexField("articleYear", new IndexValue { Integers = [dateTime.Year] },
                        culture, segment)];
            }

            return [];
        }

    }
}
