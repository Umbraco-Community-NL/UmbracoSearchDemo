namespace Site.SearchProvider.Constants;

internal static class IndexConstants
{
    public static class Variation
    {
        public const string InvariantCulture = "inv";
        
        public const string DefaultSegment = "def";
    }
    
    public static class FieldNames
    {
        public const string Id = "id";
        
        public const string ObjectType = "objectType";
        
        public const string Key = "key";
        
        public const string Culture = "culture";
        
        public const string Segment = "segment";
        
        public const string AccessKeys = "accessKeys";
        
        public const string AllTexts = "allTexts";
        
        public const string AllTextsR1 = "allTextsR1";
        
        public const string AllTextsR2 = "allTextsR2";
        
        public const string AllTextsR3 = "allTextsR3";
        
        public const string Fields = "fields"; // Note: will use underscore separator in field names for Azure Search compatibility
    }
    
    public static class FieldTypePostfix
    {
        public const string Texts = "_texts";
        
        public const string TextsR1 = "_texts_r1";
        
        public const string TextsR2 = "_texts_r2";
        
        public const string TextsR3 = "_texts_r3";
        
        public const string Keywords = "_keywords";
        
        public const string Integers = "_integers";
        
        public const string Decimals = "_decimals";
        
        public const string DateTimeOffsets = "_datetimeoffsets";
        
        public const string Sortable = "_sort";
    }
}

