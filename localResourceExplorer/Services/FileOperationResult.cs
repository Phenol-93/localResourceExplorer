namespace LocalResourceExplorer.Services;

public sealed record FileOperationResult(bool IsSuccess, bool IsMissing, string? ErrorMessage)
{
    public static FileOperationResult Success()
    {
        return new FileOperationResult(true, false, null);
    }

    public static FileOperationResult Missing(string path)
    {
        return new FileOperationResult(false, true, $"文件不存在或已移动：{path}");
    }

    public static FileOperationResult Failure(string message)
    {
        return new FileOperationResult(false, false, message);
    }
}
