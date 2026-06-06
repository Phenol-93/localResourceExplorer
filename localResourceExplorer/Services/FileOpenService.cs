using System.Diagnostics;
using System.IO;
using System.Windows;

namespace LocalResourceExplorer.Services;

public sealed class FileOpenService
{
    public FileOperationResult OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            return FileOperationResult.Missing(path);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            return FileOperationResult.Success();
        }
        catch (Exception ex)
        {
            return FileOperationResult.Failure($"无法打开文件：{ex.Message}");
        }
    }

    public string? OpenContainingFolder(string path)
    {
        if (!File.Exists(path))
        {
            return $"文件不存在或已移动：{path}";
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false
            };

            Process.Start(startInfo);
            return null;
        }
        catch (Exception ex)
        {
            return $"无法打开所在文件夹：{ex.Message}";
        }
    }

    public string? CopyPathToClipboard(string path)
    {
        if (!File.Exists(path))
        {
            return $"文件不存在或已移动：{path}";
        }

        try
        {
            Clipboard.SetText(path);
            return null;
        }
        catch (Exception ex)
        {
            return $"无法复制路径：{ex.Message}";
        }
    }
}
