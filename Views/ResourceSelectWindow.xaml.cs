using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XexTool.ViewModels;
using XexTool.Xex.Structure;

namespace XexTool.Views;

public partial class ResourceSelectWindow : Window
{
    public XexResource? SelectedResource { get; private set; }
    private readonly List<ResourceItemViewModel> _items;

    public ResourceSelectWindow(IEnumerable<XexResource> resources)
    {
        InitializeComponent();

        _items = resources.Select(r => new ResourceItemViewModel
        {
            Name = r.Name,
            Type = r.Type,
            DataLength = r.Data?.Length ?? 0,
            Resource = r,
            Thumbnail = TryLoadThumbnail(r)
        }).ToList();

        lstResources.ItemsSource = _items;

        if (_items.Count > 0)
        {
            lstResources.SelectedIndex = 0;
        }

        UpdateExportButton();
        UpdatePreview();
    }

    private static ImageSource? TryLoadThumbnail(XexResource resource)
    {
        if (resource.Data == null || resource.Data.Length == 0)
            return null;

        if (!IsImageType(resource.Type))
            return null;

        try
        {
            using var stream = new MemoryStream(resource.Data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 80; // Small thumbnail
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsImageType(string type)
    {
        return type switch
        {
            "PNG" => true,
            "JPEG" => true,
            "BMP" => true,
            "GIF" => true,
            _ => false
        };
    }

    private void lstResources_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateExportButton();
        UpdatePreview();
    }

    private void UpdateExportButton()
    {
        btnExport.IsEnabled = lstResources.SelectedItem != null;
    }

    private void UpdatePreview()
    {
        if (lstResources.SelectedItem is not ResourceItemViewModel item)
        {
            // No selection
            noSelectionPanel.Visibility = Visibility.Visible;
            previewPanel.Visibility = Visibility.Collapsed;
            return;
        }

        noSelectionPanel.Visibility = Visibility.Collapsed;
        previewPanel.Visibility = Visibility.Visible;

        var resource = item.Resource;

        lblFileSize.Text = item.SizeDisplay;

        if (resource.Data != null && resource.Data.Length > 0 && IsImageType(resource.Type))
        {
            try
            {
                using var stream = new MemoryStream(resource.Data);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                imgPreview.Source = bitmap;
                imgPreview.Visibility = Visibility.Visible;
                notImagePanel.Visibility = Visibility.Collapsed;

                lblDimensions.Text = $"{bitmap.PixelWidth} x {bitmap.PixelHeight}";
            }
            catch
            {
                ShowBinaryPreview(resource);
            }
        }
        else
        {
            // Not an image type
            ShowBinaryPreview(resource);
        }
    }

    private void ShowBinaryPreview(XexResource resource)
    {
        imgPreview.Source = null;
        imgPreview.Visibility = Visibility.Collapsed;
        notImagePanel.Visibility = Visibility.Visible;
        lblDimensions.Text = "--";
        lblBinarySize.Text = $"{resource.Data?.Length ?? 0:N0} bytes";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (lstResources.SelectedItem is ResourceItemViewModel item)
        {
            SelectedResource = item.Resource;
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
