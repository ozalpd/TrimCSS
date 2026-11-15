using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using TrimCSS.Contracts;
using TrimCSS.Engine;
using TrimCSS.GUI.Models;
using TrimCSS.Models;

namespace TrimCSS.GUI;

public partial class MainWindow : Window
{
    private const string AllFilesOption = "All CSS Files";

    private readonly ICssScanner _scanner;
    private string? _selectedFolderPath;
    private List<SelectorRowItem> _allItems = [];
    private ICollectionView? _itemsView;

    public MainWindow()
    {
        InitializeComponent();
        _scanner = new ProjectScanner();
        FileFilterCombo.ItemsSource = new[] { AllFilesOption };
        FileFilterCombo.SelectedIndex = 0;
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Web Project Folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            _selectedFolderPath = dialog.FolderName;
            FolderPathText.Text = _selectedFolderPath;
            FolderPathText.Foreground = Brushes.White;
            ScanProjectButton.IsEnabled = true;

            // Reset prior results
            _allItems = [];
            ResultsGrid.ItemsSource = null;
            ResultsGrid.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStateMessage.Text = "Click Scan Project to analyze stylesheets in this folder.";
            ResetMetrics();
        }
    }

    private async void ScanProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            return;
        }

        SetScanningState(true);

        try
        {
            string folder = _selectedFolderPath;
            ScanResult result = await Task.Run(() => _scanner.ScanProjectAsync(folder));

            UpdateMetrics(result);
            BindResults(result, folder);
        }
        catch (Exception ex)
        {
            ResultsGrid.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStateMessage.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            SetScanningState(false);
        }
    }

    private void BindResults(ScanResult result, string projectRoot)
    {
        var combined = new List<SelectorRowItem>(
            result.UnusedSelectors.Count + result.AmbiguousSelectors.Count);

        foreach (var selector in result.UnusedSelectors)
        {
            combined.Add(new SelectorRowItem(selector, isUnused: true, projectRoot));
        }

        foreach (var selector in result.AmbiguousSelectors)
        {
            combined.Add(new SelectorRowItem(selector, isUnused: false, projectRoot));
        }

        _allItems = combined;

        // Populate distinct CSS file dropdown
        var fileOptions = new List<string> { AllFilesOption };
        fileOptions.AddRange(_allItems
            .Select(i => i.RelativeFilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

        FileFilterCombo.ItemsSource = fileOptions;
        FileFilterCombo.SelectedIndex = 0;

        _itemsView = CollectionViewSource.GetDefaultView(_allItems);
        _itemsView.Filter = FilterPredicate;
        ResultsGrid.ItemsSource = _itemsView;

        RefreshViewState();
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not SelectorRowItem item)
        {
            return false;
        }

        if (FilterUnusedRadio.IsChecked == true && !item.IsUnused)
        {
            return false;
        }

        if (FilterAmbiguousRadio.IsChecked == true && item.IsUnused)
        {
            return false;
        }

        if (FileFilterCombo.SelectedItem is string selectedFile &&
            selectedFile != AllFilesOption &&
            !string.Equals(item.RelativeFilePath, selectedFile, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string query = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            bool matches =
                item.SelectorText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.RelativeFilePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.TargetClassesDisplay.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshViewState()
    {
        if (_itemsView == null)
        {
            return;
        }

        _itemsView.Refresh();
        int visibleCount = _itemsView.Cast<object>().Count();
        ResultCountText.Text = visibleCount == 1 ? "1 result" : $"{visibleCount:N0} results";

        if (visibleCount == 0)
        {
            ResultsGrid.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStateMessage.Text = _allItems.Count == 0
                ? "No unused or ambiguous selectors found — your CSS looks clean!"
                : "No selectors match your current filter.";
        }
        else
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultsGrid.Visibility = Visibility.Visible;
        }
    }

    private void UpdateMetrics(ScanResult result)
    {
        StatTotalText.Text = result.TotalCssSelectors.ToString("N0");
        StatUnusedText.Text = result.UnusedSelectors.Count.ToString("N0");
        StatAmbiguousText.Text = result.AmbiguousSelectors.Count.ToString("N0");
        StatFilesText.Text = result.ScannedFilesCount.ToString("N0");
        StatTimeText.Text = result.ExecutionTime.TotalSeconds < 1
            ? $"{result.ExecutionTime.TotalMilliseconds:0}ms"
            : $"{result.ExecutionTime.TotalSeconds:0.00}s";
    }

    private void ResetMetrics()
    {
        StatTotalText.Text = "—";
        StatUnusedText.Text = "—";
        StatAmbiguousText.Text = "—";
        StatFilesText.Text = "—";
        StatTimeText.Text = string.Empty;
        ResultCountText.Text = string.Empty;
    }

    private void SetScanningState(bool isScanning)
    {
        SelectFolderButton.IsEnabled = !isScanning;
        ScanProjectButton.IsEnabled = !isScanning && !string.IsNullOrWhiteSpace(_selectedFolderPath);
        ScanProjectButton.Content = isScanning ? "Scanning..." : "Scan Project";
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshViewState();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshViewState();

    private void FileFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshViewState();

    private void SelectAllCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_itemsView == null)
        {
            return;
        }

        bool isChecked = SelectAllCheckBox.IsChecked == true;
        foreach (SelectorRowItem item in _itemsView)
        {
            item.IsSelected = isChecked;
        }
    }
}