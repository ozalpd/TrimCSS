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
            ExportReportButton.IsEnabled = false;

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
            ScanOptions options = BuildScanOptions();
            ScanResult result = await Task.Run(() => _scanner.ScanProjectAsync(folder, options));

            UpdateMetrics(result);
            BindResults(result, folder);
            if (result.TotalCssSelectors == 0)
            {
                MessageBox.Show(this,
                                "No CSS selectors were found in the selected folder.",
                                "Scan Complete",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }
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

    private async void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_itemsView == null)
        {
            return;
        }

        List<SelectorRowItem> visibleItems = _itemsView.Cast<SelectorRowItem>().ToList();
        if (visibleItems.Count == 0)
        {
            return;
        }

        string folderName = !string.IsNullOrWhiteSpace(_selectedFolderPath)
            ? new DirectoryInfo(_selectedFolderPath).Name
            : "project";

        var dialog = new SaveFileDialog
        {
            Title = "Export TrimCSS Report",
            Filter = "CSV Report (*.csv)|*.csv|JSON Report (*.json)|*.json",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"trimcss-report-{folderName}-{DateTime.Now:yyyyMMdd-HHmm}"
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        ExportReportButton.IsEnabled = false;

        try
        {
            string ext = Path.GetExtension(dialog.FileName);
            if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            {
                string activeStatus = FilterUnusedRadio.IsChecked == true ? "Unused Only"
                    : FilterAmbiguousRadio.IsChecked == true ? "Ambiguous Only"
                    : "All Flagged";

                await ReportExporter.ExportJsonAsync(
                    dialog.FileName,
                    _selectedFolderPath,
                    activeStatus,
                    SearchBox.Text.Trim(),
                    visibleItems);
            }
            else
            {
                await ReportExporter.ExportCsvAsync(dialog.FileName, visibleItems);
            }

            MessageBox.Show(
                this,
                $"Exported {visibleItems.Count:N0} selectors to:\n{dialog.FileName}",
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
            ExportReportButton.IsEnabled = visibleItems.Count > 0;
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
        ExportReportButton.IsEnabled = !isScanning && (_itemsView?.Cast<object>().Any() ?? false);
        ExclusionFilterBox.IsEnabled = !isScanning;
        ExcludeVendorCssCheckBox.IsEnabled = !isScanning;
        ScanProjectButton.Content = isScanning ? "Scanning..." : "Scan Project";
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