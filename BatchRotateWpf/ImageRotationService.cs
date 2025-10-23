using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BatchRotateWpf;

public static class ImageRotationService
{
    public static Task RotateImagesAsync(
        IEnumerable<string> filePaths,
        string outputFolder,
        int angle,
        string suffix,
        bool overwrite,
        IProgress<RotationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return RunOnStaThreadAsync(
            () => RotateImagesInternal(filePaths, outputFolder, angle, suffix, overwrite, progress, cancellationToken),
            cancellationToken);
    }

    private static void RotateImagesInternal(
        IEnumerable<string> filePaths,
        string outputFolder,
        int angle,
        string suffix,
        bool overwrite,
        IProgress<RotationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = filePaths.ToList();
        if (files.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(outputFolder);

        var processed = 0;
        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = BuildOutputPath(outputFolder, filePath, angle, suffix, overwrite);
            RotateSingleImage(filePath, destinationPath, angle, overwrite);

            processed++;
            progress?.Report(new RotationProgress(processed, files.Count, filePath, destinationPath));
        }
    }

    private static string BuildOutputPath(string outputFolder, string sourcePath, int angle, string suffix, bool overwrite)
    {
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var sanitizedSuffix = suffix ?? string.Empty;

        if (string.IsNullOrWhiteSpace(sanitizedSuffix))
        {
            sanitizedSuffix = $"_r{angle}";
        }
        else if (sanitizedSuffix.Contains("{angle}", StringComparison.OrdinalIgnoreCase))
        {
            sanitizedSuffix = sanitizedSuffix.Replace("{angle}", angle.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        var candidate = Path.Combine(outputFolder, sourceName + sanitizedSuffix + extension);

        if (overwrite)
        {
            return candidate;
        }

        var counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(outputFolder, $"{sourceName}{sanitizedSuffix}_{counter}{extension}");
            counter++;
        }

        return candidate;
    }

    private static void RotateSingleImage(string sourcePath, string destinationPath, int angle, bool overwrite)
    {
        using var inputStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(inputStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();

        BitmapSource source = frame;
        if (angle % 360 != 0)
        {
            source = CreateRotatedSource(frame, angle);
        }

        var encoder = CreateEncoder(destinationPath);
        encoder.Frames.Add(BitmapFrame.Create(source));

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var outputStream = new FileStream(destinationPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
        encoder.Save(outputStream);
    }

    private static BitmapEncoder CreateEncoder(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder
            {
                QualityLevel = 95
            },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
    }

    private static BitmapSource CreateRotatedSource(BitmapSource source, int angle)
    {
        var normalizedAngle = angle % 360;
        if (normalizedAngle < 0)
        {
            normalizedAngle += 360;
        }

        if (normalizedAngle == 0)
        {
            return source;
        }

        var transformed = new TransformedBitmap();
        transformed.BeginInit();
        transformed.Source = source;
        transformed.Transform = new RotateTransform(normalizedAngle);
        transformed.EndInit();
        transformed.Freeze();

        return transformed;
    }

    private static Task RunOnStaThreadAsync(Action action, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.TrySetResult(null);
            }
            catch (OperationCanceledException oce)
            {
                var token = oce.CancellationToken.CanBeCanceled ? oce.CancellationToken : cancellationToken;
                tcs.TrySetCanceled(token);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}

public readonly record struct RotationProgress(int Processed, int Total, string SourcePath, string DestinationPath);
