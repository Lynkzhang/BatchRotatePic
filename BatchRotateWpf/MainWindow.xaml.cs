using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BatchRotateWpf;

public partial class MainWindow : Window
{
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
    };

    private readonly ObservableCollection<ImageItem> _images = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isProcessing;

    public MainWindow()
    {
        InitializeComponent();
        ImagesListBox.ItemsSource = _images;
        StatusTextBlock.Text = "Select a folder to begin.";
        AngleComboBox.Text = "90";
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var folder = BrowseForFolder("Select the folder that contains the images you want to rotate");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            InputFolderTextBox.Text = folder;
            LoadImages(folder);
        }
    }

    private void LoadImages_Click(object sender, RoutedEventArgs e)
    {
        var folder = InputFolderTextBox.Text;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "Please select a valid input folder.", "Input folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadImages(folder);
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = BrowseForFolder("Select the folder where rotated images will be saved");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            OutputFolderTextBox.Text = folder;
        }
    }

    private string? BrowseForFolder(string description)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var result = dialog.ShowDialog();
        return result == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void LoadImages(string folder)
    {
        _images.Clear();
        ProcessingProgressBar.Value = 0;

        if (!Directory.Exists(folder))
        {
            StatusTextBlock.Text = "Input folder does not exist.";
            return;
        }

        var files = Directory.EnumerateFiles(folder)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                _images.Add(ImageItem.Create(file));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load thumbnail for {file}. {ex.Message}");
            }
        }

        ProcessingProgressBar.Maximum = _images.Count;
        StatusTextBlock.Text = _images.Count > 0
            ? $"Loaded {_images.Count} image{(_images.Count == 1 ? string.Empty : "s")}."
            : "No images found in the selected folder.";

        if (_images.Count > 0)
        {
            ImagesListBox.SelectedIndex = 0;
        }
        else
        {
            UpdatePreview(null);
        }
    }

    private void ImagesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImagesListBox.SelectedItem is ImageItem imageItem)
        {
            UpdatePreview(imageItem);
        }
        else
        {
            UpdatePreview(null);
        }
    }

    private void UpdatePreview(ImageItem? imageItem)
    {
        if (imageItem is null)
        {
            PreviewImage.Source = null;
            PreviewFileNameTextBlock.Text = string.Empty;
            PreviewDimensionsTextBlock.Text = string.Empty;
            PreviewPathTextBlock.Text = string.Empty;
            return;
        }

        try
        {
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.UriSource = new Uri(imageItem.FilePath);
            preview.DecodePixelWidth = 960;
            preview.EndInit();
            preview.Freeze();
            PreviewImage.Source = preview;
        }
        catch
        {
            PreviewImage.Source = imageItem.Thumbnail;
        }

        PreviewFileNameTextBlock.Text = imageItem.FileName;
        PreviewDimensionsTextBlock.Text = imageItem.Dimensions;
        PreviewPathTextBlock.Text = imageItem.FilePath;
    }

    private async void RotateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
        {
            return;
        }

        if (_images.Count == 0)
        {
            MessageBox.Show(this, "Load at least one image before rotating.", "No images", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(AngleComboBox.Text, out var angle))
        {
            MessageBox.Show(this, "Enter a valid rotation angle.", "Invalid angle", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        angle = NormalizeAngle(angle);
        if (angle % 90 != 0)
        {
            MessageBox.Show(this, "The rotation angle must be a multiple of 90 degrees.", "Invalid angle", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var outputFolder = OutputFolderTextBox.Text;
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            outputFolder = InputFolderTextBox.Text;
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show(this, "Please select an output folder.", "Missing output folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            Directory.CreateDirectory(outputFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to access output folder. {ex.Message}", "Output folder", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var suffix = (SuffixTextBox.Text ?? string.Empty).Trim();
        if (suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "The suffix contains invalid file name characters.", "Invalid suffix", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var overwrite = OverwriteCheckBox.IsChecked == true;

        _isProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();
        UpdateProcessingState();
        ProcessingProgressBar.Value = 0;
        ProcessingProgressBar.Maximum = _images.Count;
        StatusTextBlock.Text = "Rotating images...";

        var progress = new Progress<RotationProgress>(progressUpdate =>
        {
            ProcessingProgressBar.Value = progressUpdate.Processed;
            var fileName = Path.GetFileName(progressUpdate.SourcePath);
            StatusTextBlock.Text = $"Processing {progressUpdate.Processed}/{progressUpdate.Total}: {fileName}";
        });

        try
        {
            await ImageRotationService.RotateImagesAsync(
                _images.Select(i => i.FilePath),
                outputFolder,
                angle,
                suffix,
                overwrite,
                progress,
                _cancellationTokenSource.Token);

            ProcessingProgressBar.Value = ProcessingProgressBar.Maximum;
            StatusTextBlock.Text = "Rotation completed.";
            MessageBox.Show(this, $"Finished rotating {_images.Count} image{(_images.Count == 1 ? string.Empty : "s")}.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Rotation cancelled.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "An error occurred.";
            MessageBox.Show(this, ex.Message, "Rotation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            UpdateProcessingState();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isProcessing)
        {
            return;
        }

        _cancellationTokenSource?.Cancel();
        CancelButton.IsEnabled = false;
        StatusTextBlock.Text = "Cancelling...";
    }

    private void UpdateProcessingState()
    {
        RotateButton.IsEnabled = !_isProcessing;
        CancelButton.IsEnabled = _isProcessing;
        AngleComboBox.IsEnabled = !_isProcessing;
        OverwriteCheckBox.IsEnabled = !_isProcessing;
        SuffixTextBox.IsEnabled = !_isProcessing;
        ImagesListBox.IsEnabled = !_isProcessing;
    }

    private static int NormalizeAngle(int angle)
    {
        var normalized = angle % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized;
    }
}

public sealed class ImageItem
{
    private ImageItem(string filePath, BitmapSource thumbnail, string dimensions, string fileSize)
    {
        FilePath = filePath;
        Thumbnail = thumbnail;
        Dimensions = dimensions;
        FileSize = fileSize;
    }

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    public BitmapSource Thumbnail { get; }

    public string Dimensions { get; }

    public string FileSize { get; }

    public static ImageItem Create(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();

        var width = frame.PixelWidth;
        var height = frame.PixelHeight;

        var dimensions = $"{width} × {height}";
        var fileInfo = new FileInfo(filePath);
        var fileSize = FormatFileSize(fileInfo.Length);

        return new ImageItem(filePath, frame, dimensions, fileSize);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
