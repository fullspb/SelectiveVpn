using Avalonia.Controls;
using Avalonia.Interactivity;
using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Desktop.Views;

public partial class ServerPickerWindow : Window
{
    private enum SortColumn
    {
        Name,
        Delay
    }

    private readonly Func<CancellationToken, Task<IReadOnlyList<VpnNodeInfo>>>? _refresh;
    private readonly Func<IEnumerable<string>, CancellationToken, Task<IReadOnlyDictionary<string, int>>>? _measureDelays;
    private readonly bool _canPing;
    private IReadOnlyList<VpnNodeInfo> _nodes = [];
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true;
    private bool _busy;
    private CancellationTokenSource? _cts;

    public ServerPickerWindow()
        : this(SubscriptionNodeCatalog.WithAuto([]), SubscriptionProfile.AutoNodeName)
    {
    }

    public ServerPickerWindow(
        IReadOnlyList<VpnNodeInfo> nodes,
        string? selectedName,
        Func<CancellationToken, Task<IReadOnlyList<VpnNodeInfo>>>? refresh = null,
        Func<IEnumerable<string>, CancellationToken, Task<IReadOnlyDictionary<string, int>>>? measureDelays = null,
        bool canPing = false)
    {
        InitializeComponent();
        _refresh = refresh;
        _measureDelays = measureDelays;
        _canPing = canPing && measureDelays is not null;
        _nodes = nodes;
        Title = UiLoc.T("Servers.Title");
        HeadingText.Text = UiLoc.T("Servers.Heading");
        HintText.Text = UiLoc.T("Servers.Hint");
        RefreshButton.Content = UiLoc.T("Ui.Refresh");
        PingButton.Content = UiLoc.T("Servers.Ping");
        CancelButton.Content = UiLoc.T("Ui.Cancel");
        SelectButton.Content = UiLoc.T("Ui.Select");
        RefreshButton.IsVisible = refresh is not null;
        PingButton.IsEnabled = _canPing;
        ToolTip.SetTip(PingButton, _canPing
            ? UiLoc.T("Servers.PingTip")
            : UiLoc.T("Servers.PingDisabledTip"));
        LoadNodes(nodes, selectedName);
    }

    public string SelectedNodeName { get; private set; } = SubscriptionProfile.AutoNodeName;

    private void LoadNodes(IReadOnlyList<VpnNodeInfo> nodes, string? selectedName)
    {
        _nodes = nodes;
        var view = SortNodes(nodes);
        UpdateHeaderLabels();
        ServerList.ItemsSource = view;
        var selected = view.FirstOrDefault(node =>
            node.Name.Equals(selectedName ?? SubscriptionProfile.AutoNodeName, StringComparison.OrdinalIgnoreCase));
        ServerList.SelectedItem = selected ?? view.FirstOrDefault();
    }

    private IReadOnlyList<VpnNodeInfo> SortNodes(IReadOnlyList<VpnNodeInfo> nodes)
    {
        var auto = nodes.Where(node => node.IsAuto);
        IEnumerable<VpnNodeInfo> rest = nodes.Where(node => !node.IsAuto);

        rest = _sortColumn switch
        {
            SortColumn.Delay when _sortAscending => rest
                .OrderBy(node => node.DelayMs is > 0 ? 0 : 1)
                .ThenBy(node => node.DelayMs is > 0 ? node.DelayMs!.Value : int.MaxValue)
                .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Delay => rest
                .OrderBy(node => node.DelayMs is > 0 ? 0 : 1)
                .ThenByDescending(node => node.DelayMs is > 0 ? node.DelayMs!.Value : int.MinValue)
                .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Name when _sortAscending => rest
                .OrderBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => rest.OrderByDescending(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };

        return auto.Concat(rest).ToList();
    }

    private void UpdateHeaderLabels()
    {
        var name = UiLoc.T("Servers.Name");
        var delay = UiLoc.T("Servers.Delay");
        NameHeaderButton.Content = _sortColumn == SortColumn.Name
            ? (_sortAscending ? $"{name} ▾" : $"{name} ▴")
            : name;
        DelayHeaderButton.Content = _sortColumn == SortColumn.Delay
            ? (_sortAscending ? $"{delay} ▾" : $"{delay} ▴")
            : delay;
    }

    private void SortByNameClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (_sortColumn == SortColumn.Name)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = SortColumn.Name;
            _sortAscending = true;
        }

        LoadNodes(_nodes, (ServerList.SelectedItem as VpnNodeInfo)?.Name);
    }

    private void SortByDelayClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (_sortColumn == SortColumn.Delay)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = SortColumn.Delay;
            _sortAscending = true;
        }

        LoadNodes(_nodes, (ServerList.SelectedItem as VpnNodeInfo)?.Name);
    }

    private async void RefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _refresh is null)
            return;

        await RunBusyAsync("Refreshing…", async ct =>
        {
            var nodes = await _refresh(ct).ConfigureAwait(true);
            LoadNodes(nodes, (ServerList.SelectedItem as VpnNodeInfo)?.Name);
            StatusText.Text = nodes.Count <= 1
                ? "Only AUTO is available."
                : $"Loaded {nodes.Count - 1} servers.";
            ErrorText.IsVisible = false;
        }).ConfigureAwait(true);
    }

    private async void PingClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || !_canPing || _measureDelays is null)
        {
            ShowError("Connect this subscription before pinging.");
            return;
        }

        var names = _nodes.Where(node => !node.IsAuto).Select(node => node.Name).ToArray();
        if (names.Length == 0)
        {
            ShowError("Nothing to ping.");
            return;
        }

        await RunBusyAsync($"Pinging {names.Length}…", async ct =>
        {
            var delays = await _measureDelays(names, ct).ConfigureAwait(true);
            var merged = SubscriptionNodeCatalog.ApplyDelays(_nodes, delays, sortByDelay: false);
            _sortColumn = SortColumn.Delay;
            _sortAscending = true;
            LoadNodes(merged, (ServerList.SelectedItem as VpnNodeInfo)?.Name);
            var ok = delays.Count(pair => pair.Value > 0);
            StatusText.Text = ok == 0
                ? "No successful pings."
                : $"Pinged {ok}/{names.Length}.";
            ErrorText.IsVisible = false;
        }).ConfigureAwait(true);
    }

    private void SelectClick(object? sender, RoutedEventArgs e) => AcceptSelection();

    private void ServerList_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        => AcceptSelection();

    private void AcceptSelection()
    {
        if (ServerList.SelectedItem is not VpnNodeInfo node)
        {
            ShowError("Pick a server.");
            return;
        }

        SelectedNodeName = node.Name;
        Close(true);
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        if (_busy && _cts is not null)
        {
            StatusText.Text = "Cancelling…";
            _cts.Cancel();
            return;
        }

        Close(false);
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> work)
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        RefreshButton.IsEnabled = false;
        PingButton.IsEnabled = false;
        StatusText.Text = status;
        ErrorText.IsVisible = false;
        try
        {
            await work(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _busy = false;
            RefreshButton.IsEnabled = _refresh is not null;
            PingButton.IsEnabled = _canPing;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
