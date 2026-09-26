using System.Collections.ObjectModel;

namespace MultiWiz.App.ViewModels;

internal static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="collection"/> contain exactly <paramref name="target"/> in that order using the fewest
    /// removes, moves and inserts, so list views keep their scroll position and focused rows.
    /// </summary>
    public static void Apply<T>(ObservableCollection<T> collection, IReadOnlyList<T> target)
        where T : class
    {
        var keep = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(collection[i]))
            {
                collection.RemoveAt(i);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            var item = target[i];
            var currentIndex = collection.IndexOf(item);
            if (currentIndex == i)
            {
                continue;
            }

            if (currentIndex >= 0)
            {
                collection.Move(currentIndex, i);
            }
            else
            {
                collection.Insert(i, item);
            }
        }
    }
}
