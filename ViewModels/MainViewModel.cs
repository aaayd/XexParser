using CommunityToolkit.Mvvm.ComponentModel;
using XexTool.Helpers;
using XexTool.Xex.Structure;

namespace XexTool.Views;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private XexFileInfo? _currentFileInfo;

    [ObservableProperty]
    private string _statusMessage = "Ready - Drop a XEX file to begin";

    [ObservableProperty]
    private string _gameTitle = "No File Loaded";

    [ObservableProperty]
    private string _titleIdDisplay = "";

    [ObservableProperty]
    private string _mediaIdDisplay = "";

    [ObservableProperty]
    private string _compressionType = "N/A";

    [ObservableProperty]
    private string _encryptionType = "N/A";

    [ObservableProperty]
    private string _imageSizeDisplay = "N/A";

    [ObservableProperty]
    private int _libraryCount;

    [ObservableProperty]
    private int _resourceCount;

    [ObservableProperty]
    private bool _canExtract;

    [ObservableProperty]
    private bool _hasImages;

    [ObservableProperty]
    private bool _hasFile;

    [ObservableProperty]
    private bool _hasTitleId;

    [ObservableProperty]
    private bool _hasMediaId;

    public bool HasNoFile => !HasFile;

    public void LoadFileInfo(string filePath, XexFileInfo info)
    {
        FilePath = filePath;
        CurrentFileInfo = info;
        HasFile = true;
        OnPropertyChanged(nameof(HasNoFile));

        string? dbGameName = null;

        if (info.ExecutionInfo != null)
        {
            dbGameName = GameDatabase.GetGameName(info.ExecutionInfo.TitleId);
        }
        
        if (!string.IsNullOrEmpty(dbGameName))
        {
            GameTitle = dbGameName;
        }
        else if (!string.IsNullOrEmpty(info.GameTitle))
        {
            GameTitle = info.GameTitle;
        }
        else if (info.ExecutionInfo != null)
        {
            GameTitle = $"Title ID: {info.ExecutionInfo.TitleIdHex}";
        }
        else
        {
            GameTitle = System.IO.Path.GetFileNameWithoutExtension(filePath);
        }

        if (info.ExecutionInfo != null)
        {
            TitleIdDisplay = $"ID: {info.ExecutionInfo.TitleIdHex}";
            MediaIdDisplay = $"Media: {info.ExecutionInfo.MediaId:X8}";
            HasTitleId = true;
            HasMediaId = true;
        }
        else
        {
            TitleIdDisplay = "";
            MediaIdDisplay = "";
            HasTitleId = false;
            HasMediaId = false;
        }

        if (info.Compression != null)
        {
            CompressionType = info.Compression.CompressionType.ToString();
            EncryptionType = info.Compression.EncryptionType.ToString();
        }
        else
        {
            CompressionType = "Unknown";
            EncryptionType = "Unknown";
        }

        ImageSizeDisplay = FormatBytes(info.ImageSize);

        LibraryCount = info.Libraries.Count;
        ResourceCount = info.Resources.Count;

        CanExtract = info.SessionKey != null;
        HasImages = info.Resources.Any(r => r.Data != null && r.Data.Length > 0);

        StatusMessage = $"Loaded: {System.IO.Path.GetFileName(filePath)}";
    }

    public void Clear()
    {
        FilePath = null;
        CurrentFileInfo = null;
        HasFile = false;
        OnPropertyChanged(nameof(HasNoFile));
        GameTitle = "No File Loaded";
        TitleIdDisplay = "";
        MediaIdDisplay = "";
        CompressionType = "N/A";
        EncryptionType = "N/A";
        ImageSizeDisplay = "N/A";
        LibraryCount = 0;
        ResourceCount = 0;
        CanExtract = false;
        HasImages = false;
        HasTitleId = false;
        HasMediaId = false;
        StatusMessage = "Ready - Drop a XEX file to begin";
    }

    private static string FormatBytes(uint bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {suffixes[order]}";
    }
}
