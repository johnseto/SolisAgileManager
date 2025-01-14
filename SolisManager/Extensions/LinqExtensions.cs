namespace SolisManager.Extensions;

public static class LinqExtensions
{
    public static IEnumerable<IEnumerable<T>> GetAdjacentGroups<T>(this IEnumerable<T> slots, Func<T, bool> predicate)
    {
        List<IEnumerable<T>> result = new();

        var matches = slots.TakeWhile(predicate).ToArray();

        if(matches.Any())
            result.Add(matches);
        
        var remainder = slots.Skip(matches.Length).SkipWhile(x => !predicate(x)).ToList();
        if(remainder.Any())
            result.AddRange(GetAdjacentGroups(remainder, predicate));

        return result;
    }

    public static T[] GetPreviousNItems<T>(this T[] source, int count, Func<T, bool> predicate)
    {
        var rootItem = Array.FindIndex(source, x => predicate(x));

        if (rootItem <= 0)
            return [];

        int lastItemIndex = rootItem - 1;
        int firstItemIndex = Math.Max(lastItemIndex - count, 0);
        return source[firstItemIndex .. lastItemIndex];
    }

}