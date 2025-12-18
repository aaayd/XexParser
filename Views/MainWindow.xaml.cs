using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using XexTool.Xex;
using XexTool.Xex.Structure;

namespace XexTool.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isLogVisible = false;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileBrowser();
    }

    private void EmptyState_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenFileBrowser();
    }

    private void OpenFileBrowser()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "XEX Files (*.xex)|*.xex|All Files (*.*)|*.*",
            Title = "Select Xbox 360 Executable"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadFile(dialog.FileName);
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                LoadFile(files[0]);
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void LoadFile(string filePath)
    {
        ShowLoading("Analyzing executable...");

        try
        {
            txtLog.Clear();
            treeInfo.Items.Clear();

            await Task.Run(() =>
            {
                using var reader = new XexReader(filePath);
                reader.OnLog += message => AppendLog(message);

                var info = reader.Parse();

                Dispatcher.Invoke(() =>
                {
                    _viewModel.LoadFileInfo(filePath, info);
                    DisplayFileInfo(info);
                });
            });
        }
        catch (Exception ex)
        {
            AppendLog($"\nError: {ex.Message}");
            MessageBox.Show($"Failed to load XEX file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            HideLoading();
        }
    }

    private void DisplayFileInfo(XexFileInfo info)
    {
        treeInfo.Items.Clear();

        var headerItem = CreateTreeItem("XEX Header", true);
        AddChildItem(headerItem, $"Magic: {info.Magic}");
        AddChildItem(headerItem, $"Module Flags: 0x{info.ModuleFlags:X8}");
        AddChildItem(headerItem, $"Data Offset: {info.DataOffset}");
        AddChildItem(headerItem, $"Reserved: {info.Reserved}");
        AddChildItem(headerItem, $"File Header Offset: {info.FileHeaderOffset}");
        AddChildItem(headerItem, $"Optional Header Entries: {info.OptionalHeaderEntries}");
        treeInfo.Items.Add(headerItem);

        var fileHeaderItem = CreateTreeItem("File Header", true);
        AddChildItem(fileHeaderItem, $"Load Address: 0x{info.LoadAddress:X8}");
        AddChildItem(fileHeaderItem, $"Image Size: 0x{info.ImageSize:X8}");
        AddChildItem(fileHeaderItem, $"Game Region: 0x{info.GameRegion:X8}");
        AddChildItem(fileHeaderItem, $"Image Flags: 0x{info.ImageFlags:X8}");
        AddChildItem(fileHeaderItem, $"Allowed Media Types: 0x{info.AllowedMediaTypes:X8}");
        treeInfo.Items.Add(fileHeaderItem);

        if (info.ExecutionInfo != null)
        {
            var execItem = CreateTreeItem("Execution ID", true);
            AddChildItem(execItem, $"Title ID: 0x{info.ExecutionInfo.TitleId:X8}");
            AddChildItem(execItem, $"Media ID: 0x{info.ExecutionInfo.MediaId:X8}");
            AddChildItem(execItem, $"Version: {info.ExecutionInfo.Version >> 16}.{info.ExecutionInfo.Version & 0xFFFF}");
            AddChildItem(execItem, $"Base Version: {info.ExecutionInfo.BaseVersion >> 16}.{info.ExecutionInfo.BaseVersion & 0xFFFF}");
            AddChildItem(execItem, $"Disc: {info.ExecutionInfo.DiscNum}/{info.ExecutionInfo.DiscsInSet}");
            AddChildItem(execItem, $"Platform: {info.ExecutionInfo.Platform}");
            AddChildItem(execItem, $"Save Game ID: 0x{info.ExecutionInfo.SaveGameId:X8}");
            treeInfo.Items.Add(execItem);
        }

        if (info.MediaTypes.Count > 0)
        {
            var mediaItem = CreateTreeItem("Allowed Media Types", true);
            foreach (var mediaType in info.MediaTypes)
            {
                AddChildItem(mediaItem, mediaType);
            }
            treeInfo.Items.Add(mediaItem);
        }

        if (info.OptionalHeaders.Count > 0)
        {
            var optionalItem = CreateTreeItem("Optional Headers", true);
            foreach (var header in info.OptionalHeaders)
            {
                var item = CreateTreeItem($"0x{header.ID:X8} - {header.Description}");
                AddChildItem(item, $"Data: 0x{header.Data:X}");
                if (!string.IsNullOrEmpty(header.DecodedValue))
                {
                    AddChildItem(item, $"Value: {header.DecodedValue}");
                }
                optionalItem.Items.Add(item);
            }
            treeInfo.Items.Add(optionalItem);
        }

        if (info.Libraries.Count > 0)
        {
            var libItem = CreateTreeItem("Libraries", true);
            foreach (var lib in info.Libraries)
            {
                string approved = lib.IsApproved ? "approved" : "unapproved";
                AddChildItem(libItem, $"{lib.Name} - {lib.Version1}.{lib.Version2}.{lib.Version3}.{lib.CleanVersion4} ({approved})");
            }
            treeInfo.Items.Add(libItem);
        }

        if (info.Compression != null)
        {
            var compItem = CreateTreeItem("Compression Info", true);
            AddChildItem(compItem, $"Encryption Type: {info.Compression.EncryptionType}");
            AddChildItem(compItem, $"Compression Type: {info.Compression.CompressionType}");
            if (info.Compression.CompressionType == XeCompressionType.Compressed)
            {
                AddChildItem(compItem, $"Compression Window: 0x{info.Compression.CompressionWindow:X8}");
                AddChildItem(compItem, $"Block Size: 0x{info.Compression.BlockSize:X8}");
                if (info.Compression.Hash.Any(b => b != 0))
                {
                    AddChildItem(compItem, $"Hash: {BitConverter.ToString(info.Compression.Hash).Replace("-", " ")}");
                }
            }
            treeInfo.Items.Add(compItem);
        }

        if (info.SessionKey != null)
        {
            var keyItem = CreateTreeItem("Session Key", true);
            AddChildItem(keyItem, BitConverter.ToString(info.SessionKey).Replace("-", " "));
            treeInfo.Items.Add(keyItem);
        }

        if (!string.IsNullOrEmpty(info.BoundPathname))
        {
            var pathItem = CreateTreeItem("Bound Pathname", true);
            AddChildItem(pathItem, info.BoundPathname);
            treeInfo.Items.Add(pathItem);
        }

        if (!string.IsNullOrEmpty(info.GameTitle))
        {
            var titleItem = CreateTreeItem("Game Title", true);
            AddChildItem(titleItem, info.GameTitle);
            treeInfo.Items.Add(titleItem);
        }
        
        if (info.Resources.Count > 0)
        {
            var resItem = CreateTreeItem("Resources / Images", true);
            foreach (var res in info.Resources)
            {
                string sizeInfo = res.Data != null ? $"{res.Data.Length} bytes" : "embedded in PE";
                string typeInfo = res.Type == "PE_EMBEDDED" ? "PE Embedded" : res.Type;
                var item = CreateTreeItem($"{res.Name} ({typeInfo}, {sizeInfo})");
                AddChildItem(item, $"Virtual Address: 0x{res.Offset:X}");
                AddChildItem(item, $"Size: {res.Size}");
                if (res.Type == "PE_EMBEDDED")
                {
                    AddChildItem(item, "NOTE: Extract PE first to access this resource");
                }
                item.Tag = res;
                resItem.Items.Add(item);
            }
            treeInfo.Items.Add(resItem);
        }
    }

    private TreeViewItem CreateTreeItem(string header, bool expanded = false)
    {
        var item = new TreeViewItem
        {
            Header = header,
            IsExpanded = expanded,
            Foreground = FindResource("TextPrimaryBrush") as Brush
        };
        return item;
    }

    private void AddChildItem(TreeViewItem parent, string text)
    {
        var child = new TreeViewItem
        {
            Header = text,
            Foreground = FindResource("TextSecondaryBrush") as Brush
        };
        parent.Items.Add(child);
    }

    private async void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentFileInfo == null || _viewModel.FilePath == null)
        {
            MessageBox.Show("Please load a XEX file first.", "No File",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PE Files (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = "Save Extracted PE File",
            FileName = Path.GetFileNameWithoutExtension((string?) _viewModel.FilePath) + ".exe"
        };

        if (dialog.ShowDialog() == true)
        {
            await ExtractPEAsync(dialog.FileName);
        }
    }

    private async Task ExtractPEAsync(string outputPath)
    {
        if (_viewModel.CurrentFileInfo == null || _viewModel.FilePath == null) return;

        ShowLoading("Extracting PE...");
        btnExtract.IsEnabled = false;

        try
        {
            AppendLog("\n--- Starting Extraction ---");

            await Task.Run(() =>
            {
                using var reader = new XexReader(_viewModel.FilePath);
                var info = reader.Parse();
                reader.ExtractPE(outputPath, info, msg => AppendLog(msg));
            });

            AppendLog("\n--- Extraction Complete ---");
            MessageBox.Show($"PE file extracted successfully to:\n{outputPath}",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"\nExtraction Error: {ex.Message}");
            MessageBox.Show($"Failed to extract PE file:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            HideLoading();
            btnExtract.IsEnabled = _viewModel.CanExtract;
        }
    }

    private void ExportImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentFileInfo == null)
        {
            MessageBox.Show("Please load a XEX file first.", "No File",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var resources = Enumerable
            .Where<XexResource>(_viewModel.CurrentFileInfo.Resources, r => r.Data != null && r.Data.Length > 0)
            .ToList();

        if (resources.Count == 0)
        {
            MessageBox.Show("No exportable images found in this XEX file.", "No Images",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (resources.Count > 1)
        {
            var selectWindow = new ResourceSelectWindow(resources);
            selectWindow.Owner = this;
            if (selectWindow.ShowDialog() == true && selectWindow.SelectedResource != null)
            {
                ExportResource(selectWindow.SelectedResource);
            }
        }
        else
        {
            ExportResource(resources[0]);
        }
    }

    private void ExportResource(XexResource resource)
    {
        if (resource.Data == null) return;

        string extension = resource.Type switch
        {
            "PNG" => ".png",
            "JPEG" => ".jpg",
            "DDS" => ".dds",
            "BMP" => ".bmp",
            "GIF" => ".gif",
            _ => ".bin"
        };

        string filter = resource.Type switch
        {
            "PNG" => "PNG Files (*.png)|*.png",
            "JPEG" => "JPEG Files (*.jpg)|*.jpg",
            "DDS" => "DDS Files (*.dds)|*.dds",
            "BMP" => "BMP Files (*.bmp)|*.bmp",
            "GIF" => "GIF Files (*.gif)|*.gif",
            _ => "Binary Files (*.bin)|*.bin"
        };

        var dialog = new SaveFileDialog
        {
            Filter = filter + "|All Files (*.*)|*.*",
            Title = "Export Image",
            FileName = Path.GetFileNameWithoutExtension(resource.Name) + extension
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllBytes(dialog.FileName, resource.Data);
                AppendLog($"\nImage exported to: {dialog.FileName}");
                MessageBox.Show($"Image exported successfully to:\n{dialog.FileName}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"\nExport Error: {ex.Message}");
                MessageBox.Show($"Failed to export image:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        txtLog.Clear();
    }

    private void ToggleLogButton_Click(object sender, RoutedEventArgs e)
    {
        _isLogVisible = !_isLogVisible;

        if (_isLogVisible)
        {
            colSplitter.Width = new GridLength(8);
            colLog.Width = new GridLength(1, GridUnitType.Star);
            colLog.MinWidth = 300;
            logSplitter.Visibility = Visibility.Visible;
            logPanel.Visibility = Visibility.Visible;
            txtLogToggle.Text = "Hide Log";
        }
        else
        {
            colSplitter.Width = new GridLength(0);
            colLog.Width = new GridLength(0);
            colLog.MinWidth = 0;
            logSplitter.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Collapsed;
            txtLogToggle.Text = "Show Log";
        }
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            txtLog.AppendText(message + Environment.NewLine);
            txtLog.ScrollToEnd();
        });
    }

    private void ShowLoading(string text = "Processing...")
    {
        lblLoadingText.Text = text;
        loadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoading()
    {
        loadingOverlay.Visibility = Visibility.Collapsed;
    }
}
