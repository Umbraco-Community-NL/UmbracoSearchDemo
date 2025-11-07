namespace Umbraco.AzureSearch.Extensions;

internal static class EnumerableExtensions
{
    public static IEnumerable<T>? NullIfEmpty<T>(this IEnumerable<T> enumerable)
    {
        T[] enumerableAsArray = enumerable as T[] ?? enumerable.ToArray();
        return enumerableAsArray.Length > 0 ? enumerableAsArray : null;
    }
}

