namespace YouTubeViewer;

public enum TabCloseScope
{
    Current,
    Left,
    Right,
    Others,
    All,
}

public static class TabOperations
{
    // Snapshot before closing: removing tabs must not change the chosen group.
    public static T[] SelectToClose<T>(IReadOnlyList<T> tabs, T anchor, TabCloseScope scope)
    {
        var index = -1;
        for (var i = 0; i < tabs.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(tabs[i], anchor))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return Array.Empty<T>();
        }

        return tabs.Where((_, i) => scope switch
        {
            TabCloseScope.Current => i == index,
            TabCloseScope.Left => i < index,
            TabCloseScope.Right => i > index,
            TabCloseScope.Others => i != index,
            TabCloseScope.All => true,
            _ => false,
        }).ToArray();
    }

    public static int MoveDestination(int sourceIndex, int targetIndex, bool afterTarget)
    {
        var insertionIndex = targetIndex + (afterTarget ? 1 : 0);
        return insertionIndex > sourceIndex ? insertionIndex - 1 : insertionIndex;
    }
}
