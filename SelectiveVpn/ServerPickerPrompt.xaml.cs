using System.Windows;
using System.Windows.Controls;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class ServerPickerPrompt : Window
{
    private enum SortColumn
    {
        Name,
        Delay
    }

    private readonly Func<CancellationToken, Task<IReadOnlyList<VpnNodeInfo>>>? _refresh;
    private readonly Func<IEnumerable<string>, CancellationToken, Task<IReadOnlyDictionary<string, int>>>? _measureDelays;
    private readonly bool _canPing;
    private IReadOnlyList<VpnNodeInfo> _nodes;
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true;
    private bool _busy;
    private CancellationTokenSource? _cts;

    public ServerPickerPrompt(
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
        RefreshButton.Visibility = refresh is null ? Visibility.Collapsed : Visibility.Visible;
        // Ширина кастомного ScrollBar из App.xaml.
        HeaderScrollSpacer.Width = new GridLength(16);
        ApplyPingAvailability();
        LoadNodes(nodes, selectedName);
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) => TitleBarTheme.ApplyFromResources(this);
    }

    public string SelectedNodeName { get; private set; } = SubscriptionProfile.AutoNodeName;

    private void ApplyPingAvailability()
    {
        PingButton.IsEnabled = _canPing && !_busy;
        ToolTipService.SetShowOnDisabled(PingButton, true);
        PingButton.ToolTip = _canPing
            ? "Измерить задержку узлов через запущенный Mihomo"
            : "Кнопка станет активной после подключения к туннелю";
    }

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
        NameHeaderButton.Content = _sortColumn == SortColumn.Name
            ? (_sortAscending ? "Имя ▾" : "Имя ▴")
            : "Имя";
        DelayHeaderButton.Content = _sortColumn == SortColumn.Delay
            ? (_sortAscending ? "Задержка ▾" : "Задержка ▴")
            : "Задержка";
    }

    private void SortByNameClick(object sender, RoutedEventArgs e)
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

        var current = (ServerList.SelectedItem as VpnNodeInfo)?.Name;
        LoadNodes(_nodes, current);
    }

    private void SortByDelayClick(object sender, RoutedEventArgs e)
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

        var current = (ServerList.SelectedItem as VpnNodeInfo)?.Name;
        LoadNodes(_nodes, current);
    }

    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _refresh is null)
            return;

        await RunBusyAsync("Обновляю список серверов…", async ct =>
        {
            var nodes = await _refresh(ct).ConfigureAwait(true);
            var current = (ServerList.SelectedItem as VpnNodeInfo)?.Name;
            LoadNodes(nodes, current);
            SetStatus(nodes.Count <= 1
                ? "Пока только режим авто — кэш узлов пуст. Подключитесь, чтобы Mihomo загрузил подписку."
                : $"Загружено узлов: {nodes.Count - 1}.");
        }).ConfigureAwait(true);
    }

    private async void PingClick(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canPing || _measureDelays is null)
        {
            ShowError("Ping доступен только когда маршрутизация уже запущена.");
            return;
        }

        var names = _nodes.Where(node => !node.IsAuto).Select(node => node.Name).ToArray();
        if (names.Length == 0)
        {
            ShowError("Нет серверов для измерения.");
            return;
        }

        await RunBusyAsync($"Пингую {names.Length} серверов…", async ct =>
        {
            var delays = await _measureDelays(names, ct).ConfigureAwait(true);
            var current = (ServerList.SelectedItem as VpnNodeInfo)?.Name;
            var merged = SubscriptionNodeCatalog.ApplyDelays(_nodes, delays, sortByDelay: false);
            _sortColumn = SortColumn.Delay;
            _sortAscending = true;
            LoadNodes(merged, current);

            var ok = delays.Count(pair => pair.Value > 0);
            var best = delays.Where(pair => pair.Value > 0).OrderBy(pair => pair.Value).FirstOrDefault();
            SetStatus(ok == 0
                ? "Ping завершён: ни один узел не ответил вовремя."
                : best.Key is null
                    ? $"Ping завершён: ответили {ok} из {names.Length}."
                    : $"Ping завершён: ответили {ok} из {names.Length}. Лучший: {best.Key} ({best.Value} мс).");
        }).ConfigureAwait(true);
    }

    private void SelectClick(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not VpnNodeInfo node)
        {
            ShowError("Выберите сервер из списка.");
            return;
        }

        SelectedNodeName = node.Name;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_busy && _cts is not null)
        {
            SetStatus("Отмена…");
            _cts.Cancel();
            return;
        }

        DialogResult = false;
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> action)
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        RefreshButton.IsEnabled = false;
        SelectButton.IsEnabled = false;
        NameHeaderButton.IsEnabled = false;
        DelayHeaderButton.IsEnabled = false;
        ApplyPingAvailability();
        SetStatus(status);
        try
        {
            await action(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ShowError("Операция отменена.");
        }
        catch (Exception)
        {
            ShowError("Не удалось выполнить операцию.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _busy = false;
            RefreshButton.IsEnabled = _refresh is not null;
            SelectButton.IsEnabled = true;
            NameHeaderButton.IsEnabled = true;
            DelayHeaderButton.IsEnabled = true;
            ApplyPingAvailability();
        }
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
    }
}
