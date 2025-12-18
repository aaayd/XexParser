using System.Windows.Media;
using XexTool.Xex.Structure;

namespace XexTool.ViewModels;

public class ResourceItemViewModel
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int DataLength { get; set; }
    public string SizeDisplay => FormatBytes(DataLength);
    public XexResource Resource { get; set; } = null!;
    public ImageSource? Thumbnail { get; set; }
    public bool HasNoThumbnail => Thumbnail == null;

    private static string FormatBytes(int bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}