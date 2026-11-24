using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TrimCSS.Contracts;
using TrimCSS.Engine;
using TrimCSS.GUI.Models;
using TrimCSS.GUI.Services;
using TrimCSS.Models;

namespace TrimCSS.GUI;

public partial class MainWindow : Window
{
    private const string AllFilesOption = "All CSS Files";

    private readonly ICssScanner _scanner;
    private string? _selectedFolderPath;
    private List<SelectorRowItem> _allItems = [];
    private ICollectionView? _itemsView;
    private List<CssFileRowItem> _allCssFiles = [];
    private ICollectionView? _cssFilesView;
    private ScanResult? _lastScanResult;
    private bool _isUiInitialized;

    public MainWindow()
    {
        InitializeComponent();
        _isUiInitialized = true;
        _scanner = new ProjectScanner();
        FileFilterCombo.ItemsSource = new[] { AllFilesOption };
        FileFilterCombo.SelectedIndex = 0;
    }

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
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
            ExportReportButton.IsEnabled = false;

            // Reset prior results
            _allItems = [];
            _allCssFiles = [];
            _lastScanResult = null;
            ResultsGrid.ItemsSource = null;
            ResultsGrid.Visibility = Visibility.Collapsed;
            CssFilesGrid.ItemsSource = null;
            CssFilesGrid.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStateMessage.Text = "Scanning project stylesheets...";
            ViewModeCssFilesRadio.Content = "CSS Files";
            ViewModeSelectorsRadio.IsChecked = true;
            ResetMetrics();

            await RunScanAsync();
        }
    }

    private async void ScanProjectButton_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            return;
        }

        SetScanningState(true);

        try
        {
            string folder = _selectedFolderPath;
            ScanOptions options = BuildScanOptions();
            ScanResult result = await Task.Run(() => _scanner.ScanProjectAsync(folder, options));
            _lastScanResult = result;

            UpdateMetrics(result);
            BindResults(result, folder);

            if (result.TotalCssSelectors == 0 && result.TotalCssFiles == 0)
            {
                MessageBox.Show(this,
                    "No CSS files or selectors were found in the selected folder.",
                    "Scan Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ResultsGrid.Visibility = Visibility.Collapsed;
            CssFilesGrid.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStateMessage.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            SetScanningState(false);
        }
    }

    private async void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        bool isCssFilesMode = ViewModeCssFilesRadio.IsChecked == true;
        if (isCssFilesMode && _cssFilesView == null)
        {
            return;
        }
        if (!isCssFilesMode && _itemsView == null)
        {
            return;
        }

        var visibleSelectors = _itemsView?.Cast<SelectorRowItem>().ToList() ?? [];
        var visibleCssFiles = _cssFilesView?.Cast<CssFileRowItem>().ToList() ?? [];

        int exportCount = isCssFilesMode ? visibleCssFiles.Count : visibleSelectors.Count;
        if (exportCount == 0)
        {
            return;
        }

        string folderName = !string.IsNullOrWhiteSpace(_selectedFolderPath)
            ? new DirectoryInfo(_selectedFolderPath).Name
            : "project";

        string modePrefix = isCssFilesMode ? "trimcss-cssfiles" : "trimcss-report";
        var dialog = new SaveFileDialog
        {
            Title = isCssFilesMode ? "Export TrimCSS Unused CSS Files Report" : "Export TrimCSS Report",
            Filter = "CSV Report (*.csv)|*.csv|JSON Report (*.json)|*.json",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"{modePrefix}-{folderName}-{DateTime.Now:yyyyMMdd-HHmm}"
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        ExportReportButton.IsEnabled = false;

        try
        {
            string ext = Path.GetExtension(dialog.FileName);
            string activeStatus = FilterUnusedRadio.IsChecked == true ? "Unused Only"
                : FilterAmbiguousRadio.IsChecked == true ? "Ambiguous Only"
                : "All Flagged";

            if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            {
                await ReportExporter.ExportJsonAsync(
                    dialog.FileName,
                    _selectedFolderPath,
                    activeStatus,
                    SearchBox.Text.Trim(),
                    selectorItems: visibleSelectors,
                    cssFileItems: visibleCssFiles,
                    scanResult: _lastScanResult,
                    activeView: isCssFilesMode ? "CSS Files" : "Selectors");
            }
            else
            {
                if (isCssFilesMode)
                {
                    await ReportExporter.ExportCssFilesCsvAsync(dialog.FileName, visibleCssFiles);
                }
                else
                {
                    await ReportExporter.ExportCsvAsync(dialog.FileName, visibleSelectors);
                }
            }

            string itemType = isCssFilesMode ? (exportCount == 1 ? "CSS file" : "CSS files") : (exportCount == 1 ? "selector" : "selectors");
            MessageBox.Show(
                this,
                $"Exported {exportCount:N0} {itemType} to:\n{dialog.FileName}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to export report: {ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportReportButton.IsEnabled = isCssFilesMode ? visibleCssFiles.Count > 0 : visibleSelectors.Count > 0;
        }
    }

    private void BindResults(ScanResult result, string projectRoot)
    {
        // 1. Bind Selectors
        var combinedSelectors = new List<SelectorRowItem>(
            result.UnusedSelectors.Count + result.AmbiguousSelectors.Count);

        foreach (var selector in result.UnusedSelectors)
        {
            combinedSelectors.Add(new SelectorRowItem(selector, isUnused: true, projectRoot));
        }

        foreach (var selector in result.AmbiguousSelectors)
        {
            combinedSelectors.Add(new SelectorRowItem(selector, isUnused: false, projectRoot));
        }

        _allItems = combinedSelectors;

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

        // 2. Bind CSS Files
        var combinedCssFiles = new List<CssFileRowItem>(
            result.UnusedCssFiles.Count + result.AmbiguousCssFiles.Count);

        foreach (var file in result.UnusedCssFiles)
        {
            combinedCssFiles.Add(new CssFileRowItem(file));
        }

        foreach (var file in result.AmbiguousCssFiles)
        {
            combinedCssFiles.Add(new CssFileRowItem(file));
        }

        _allCssFiles = combinedCssFiles;
        _cssFilesView = CollectionViewSource.GetDefaultView(_allCssFiles);
        _cssFilesView.Filter = CssFileFilterPredicate;
        CssFilesGrid.ItemsSource = _cssFilesView;

        // Update tab header badge
        int flaggedFiles = _allCssFiles.Count;
        ViewModeCssFilesRadio.Content = flaggedFiles > 0 ? $"CSS Files ({flaggedFiles})" : "CSS Files";

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

    private bool CssFileFilterPredicate(object obj)
    {
        if (obj is not CssFileRowItem item)
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

        string query = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            bool matches =
                item.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Details.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshViewState()
    {
        if (!_isUiInitialized)
        {
            return;
        }

        bool isCssFilesMode = ViewModeCssFilesRadio.IsChecked == true;
        FileFilterCombo.Visibility = isCssFilesMode ? Visibility.Collapsed : Visibility.Visible;

        if (isCssFilesMode)
        {
            ResultsGrid.Visibility = Visibility.Collapsed;

            if (_cssFilesView == null)
            {
                ResultCountText.Text = string.Empty;
                ExportReportButton.IsEnabled = false;
                CssFilesGrid.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
                return;
            }

            _cssFilesView.Refresh();
            int visibleCount = _cssFilesView.Cast<object>().Count();
            ResultCountText.Text = visibleCount == 1 ? "1 CSS file" : $"{visibleCount:N0} CSS files";
            ExportReportButton.IsEnabled = visibleCount > 0;

            if (visibleCount == 0)
            {
                CssFilesGrid.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
                EmptyStateMessage.Text = _allCssFiles.Count == 0
                    ? "No unused or ambiguous CSS files found — all stylesheets are referenced!"
                    : "No CSS files match your current filter.";
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                CssFilesGrid.Visibility = Visibility.Visible;
            }
        }
        else
        {
            CssFilesGrid.Visibility = Visibility.Collapsed;

            if (_itemsView == null)
            {
                ResultCountText.Text = string.Empty;
                ExportReportButton.IsEnabled = false;
                ResultsGrid.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
                return;
            }

            _itemsView.Refresh();
            int visibleCount = _itemsView.Cast<object>().Count();
            ResultCountText.Text = visibleCount == 1 ? "1 selector" : $"{visibleCount:N0} selectors";
            ExportReportButton.IsEnabled = visibleCount > 0;

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
    }

    private void UpdateMetrics(ScanResult result)
    {
        StatTotalText.Text = result.TotalCssSelectors.ToString("N0");
        StatUnusedText.Text = result.UnusedSelectors.Count.ToString("N0");
        StatAmbiguousText.Text = result.AmbiguousSelectors.Count.ToString("N0");

        int unusedFiles = result.UnusedCssFiles.Count;
        StatUnusedCssFilesText.Text = $"{unusedFiles:N0} / {result.TotalCssFiles:N0}";
        StatUnusedCssFilesText.Foreground = unusedFiles > 0
            ? (Brush)FindResource("RedBrush")
            : (Brush)FindResource("TextSecondaryBrush");

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
        StatUnusedCssFilesText.Text = "—";
        StatUnusedCssFilesText.Foreground = (Brush)FindResource("TextSecondaryBrush");
        StatFilesText.Text = "—";
        StatTimeText.Text = string.Empty;
        ResultCountText.Text = string.Empty;
    }

    private void SetScanningState(bool isScanning)
    {
        SelectFolderButton.IsEnabled = !isScanning;
        ScanProjectButton.IsEnabled = !isScanning && !string.IsNullOrWhiteSpace(_selectedFolderPath);
        ExportReportButton.IsEnabled = !isScanning &&
            ((ViewModeCssFilesRadio.IsChecked == true ? _cssFilesView?.Cast<object>().Any() : _itemsView?.Cast<object>().Any()) ?? false);
        ExclusionFilterBox.IsEnabled = !isScanning;
        ExcludeVendorCssCheckBox.IsEnabled = !isScanning;
        ViewModeSelectorsRadio.IsEnabled = !isScanning;
        ViewModeCssFilesRadio.IsEnabled = !isScanning;
        ScanProjectButton.Content = isScanning
                                  ? "Scanning..."
                                  : (_lastScanResult != null ? "Rescan Project" : "Scan Project");
    }

    private ScanOptions BuildScanOptions()
    {
        var options = new ScanOptions
        {
            ExcludedFolders = new List<string>(),
            ExcludedFilePatterns = new List<string>()
        };

        string text = ExclusionFilterBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var tokens = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (token.Contains('*') || token.Contains('?') || token.EndsWith(".css", StringComparison.OrdinalIgnoreCase) || token.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    if (!options.ExcludedFilePatterns.Contains(token, StringComparer.OrdinalIgnoreCase))
                    {
                        options.ExcludedFilePatterns.Add(token);
                    }
                }
                else
                {
                    if (!options.ExcludedFolders.Contains(token, StringComparer.OrdinalIgnoreCase))
                    {
                        options.ExcludedFolders.Add(token);
                    }
                }
            }
        }

        if (ExcludeVendorCssCheckBox.IsChecked == true)
        {
            string[] vendorFilePatterns = ["bootstrap*.css", "tailwind*.css", "font-awesome*.css", "fontawesome*.css", "vendor*.css", "*vendor*.css"];
            foreach (var pattern in vendorFilePatterns)
            {
                if (!options.ExcludedFilePatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                {
                    options.ExcludedFilePatterns.Add(pattern);
                }
            }

            string[] vendorFolders = ["vendor", "vendors"];
            foreach (var folder in vendorFolders)
            {
                if (!options.ExcludedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    options.ExcludedFolders.Add(folder);
                }
            }
        }

        return options;
    }

    private void ViewModeChanged(object sender, RoutedEventArgs e) => RefreshViewState();

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

    private void SelectAllCssFilesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_cssFilesView == null)
        {
            return;
        }

        bool isChecked = SelectAllCssFilesCheckBox.IsChecked == true;
        foreach (CssFileRowItem item in _cssFilesView)
        {
            item.IsSelected = isChecked;
        }
    }
}
