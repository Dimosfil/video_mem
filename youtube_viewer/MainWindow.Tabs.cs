using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace YouTubeViewer;

public partial class MainWindow
{
    private Point? _tabDragStart;
    private BrowserTab? _tabDragSource;
    private bool _reorderingTabs;

    private void ConfigureTabInteractions(BrowserTab tab)
    {
        var item = tab.Item;
        var menu = (ContextMenu)FindResource("TabContextMenu");
        menu.DataContext = tab;
        menu.Opened += (_, _) =>
        {
            foreach (var command in menu.Items.OfType<MenuItem>())
            {
                command.IsEnabled = command.Tag is TabCloseScope scope
                    ? TabOperations.SelectToClose(_tabs, tab, scope).Length > 0
                    : _closedTabs.Count > 0;
            }
        };
        item.ContextMenu = menu;
        item.AllowDrop = true;
        item.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _tabDragSource = null;
            _tabDragStart = null;
            // The close button must remain a click target, never a drag handle.
            for (var node = e.OriginalSource as DependencyObject;
                 node is not null && node != item;
                 node = VisualTreeHelper.GetParent(node))
            {
                if (node is ButtonBase)
                {
                    return;
                }
            }

            _tabDragSource = tab;
            _tabDragStart = e.GetPosition(Tabs);
        };
        item.PreviewMouseLeftButtonUp += (_, _) => ClearTabDrag();
        item.MouseLeave += (_, _) =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                ClearTabDrag();
            }
        };
        item.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _tabDragStart is not { } start ||
                !ReferenceEquals(_tabDragSource, tab))
            {
                return;
            }

            var position = e.GetPosition(Tabs);
            if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            ClearTabDrag();
            try
            {
                item.Opacity = 0.6;
                DragDrop.DoDragDrop(item, new DataObject(typeof(BrowserTab), tab), DragDropEffects.Move);
            }
            finally
            {
                item.Opacity = 1;
                ClearTabDrag();
            }
            e.Handled = true;
        };
        item.PreviewDragOver += (_, e) =>
        {
            e.Effects = GetDraggedTab(e) is not null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        item.PreviewDrop += (_, e) =>
        {
            if (GetDraggedTab(e) is { } source)
            {
                MoveTab(source, tab, e.GetPosition(item).X >= item.ActualWidth / 2);
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        };
    }

    private void ClearTabDrag()
    {
        _tabDragStart = null;
        _tabDragSource = null;
    }

    private BrowserTab? GetDraggedTab(DragEventArgs e) =>
        e.Data.GetDataPresent(typeof(BrowserTab)) &&
        e.Data.GetData(typeof(BrowserTab)) is BrowserTab tab && _tabs.Contains(tab)
            ? tab : null;

    private void MoveTab(BrowserTab source, BrowserTab target, bool afterTarget)
    {
        var sourceIndex = _tabs.IndexOf(source);
        var targetIndex = _tabs.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var destination = TabOperations.MoveDestination(sourceIndex, targetIndex, afterTarget);
        if (destination == sourceIndex)
        {
            return;
        }

        var selected = Tabs.SelectedItem;
        _reorderingTabs = true;
        try
        {
            _tabs.RemoveAt(sourceIndex);
            _tabs.Insert(destination, source);
            Tabs.Items.Remove(source.Item);
            Tabs.Items.Insert(destination, source.Item);
            Tabs.SelectedItem = selected;
        }
        finally
        {
            _reorderingTabs = false;
        }
        UpdateChrome();
        SaveSession();
    }

    private void CloseTabsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: BrowserTab anchor, Tag: TabCloseScope scope })
        {
            var closing = TabOperations.SelectToClose(_tabs, anchor, scope);
            // Select a survivor once so bulk closing does not activate each doomed page.
            if (CurrentTab is { } current && closing.Contains(current) && !closing.Contains(anchor))
            {
                Tabs.SelectedItem = anchor.Item;
            }

            foreach (var tab in closing)
            {
                CloseTab(tab);
            }
        }
    }

    private async void ReopenTabMenu_Click(object sender, RoutedEventArgs e) =>
        await ReopenClosedTabAsync();
}
