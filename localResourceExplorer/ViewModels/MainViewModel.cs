using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalResourceExplorer.Models;
using LocalResourceExplorer.Repositories;
using LocalResourceExplorer.Services;
using LocalResourceExplorer.Views;
using Microsoft.Win32;
using CollectionModel = LocalResourceExplorer.Models.Collection;

namespace LocalResourceExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string AllResourcesViewTitle = "全部资源";
    private const string ViewAll = "all";
    private const string ViewFavorites = "favorites";
    private const string ViewUnorganized = "unorganized";
    private const string ViewRecentAdded = "recent_added";
    private const string ViewRecentOpened = "recent_opened";

    private readonly AppSettingsRepository appSettingsRepository;
    private readonly DatabaseService databaseService = new();
    private readonly CollectionRepository collectionRepository;
    private readonly ExportBackupService exportBackupService;
    private readonly FileOpenService fileOpenService = new();
    private readonly MediaInfoService mediaInfoService = new();
    private readonly MissingFileChecker missingFileChecker;
    private readonly ResourceScanner resourceScanner = new();
    private readonly ResourceRepository resourceRepository;
    private readonly TagRepository tagRepository;
    private readonly ThemeService themeService = new();
    private readonly AiService aiService;
    private CancellationTokenSource? searchDebounceCancellationTokenSource;
    private CancellationTokenSource? scanCancellationTokenSource;
    private string currentSortBy = "imported_at";
    private string currentViewFilter = ViewAll;
    private int skippedCount;

    public MainViewModel()
    {
        appSettingsRepository = new AppSettingsRepository(databaseService);
        aiService = new AiService(appSettingsRepository);
        collectionRepository = new CollectionRepository(databaseService);
        exportBackupService = new ExportBackupService(databaseService);
        resourceRepository = new ResourceRepository(databaseService);
        tagRepository = new TagRepository(databaseService);
        missingFileChecker = new MissingFileChecker(resourceRepository);
        _ = InitializeAndLoadResourcesAsync();
    }

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isAddResourceMenuOpen;

    [ObservableProperty]
    private bool isSortMenuOpen;

    [ObservableProperty]
    private bool isExportMenuOpen;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private bool isAiBusy;

    [ObservableProperty]
    private CollectionModel? selectedCollection;

    [ObservableProperty]
    private TagSummary? selectedTag;

    [ObservableProperty]
    private string sortButtonText = "排序：导入时间 ↓";

    [ObservableProperty]
    private ResourceItem? selectedResource;

    [ObservableProperty]
    private int selectedResourceCount;

    [ObservableProperty]
    private string statusText = "正在加载资源...";

    [ObservableProperty]
    private string themeButtonText = "深色";

    [ObservableProperty]
    private string viewTitle = AllResourcesViewTitle;

    public ObservableCollection<CollectionModel> Collections { get; } = [];

    public ObservableCollection<ResourceItem> Resources { get; } = [];

    public ObservableCollection<CollectionModel> SelectedResourceCollections { get; } = [];

    public ObservableCollection<TagSummary> Tags { get; } = [];

    public ObservableCollection<Tag> SelectedResourceTags { get; } = [];

    public ObservableCollection<ResourceItem> SelectedResources { get; } = [];

    public bool SortDescending { get; private set; } = true;

    partial void OnSearchTextChanged(string value)
    {
        searchDebounceCancellationTokenSource?.Cancel();
        searchDebounceCancellationTokenSource?.Dispose();
        searchDebounceCancellationTokenSource = new CancellationTokenSource();

        _ = RefreshResourcesWithDebounceAsync(searchDebounceCancellationTokenSource.Token);
    }

    partial void OnSelectedResourceChanged(ResourceItem? value)
    {
        SelectedResourceCount = value is null ? 0 : 1;
        _ = LoadSelectedResourceCollectionsAsync(value);
        _ = LoadSelectedResourceTagsAsync(value);
    }

    public void SetSelectedResources(IEnumerable<ResourceItem> resources)
    {
        SelectedResources.Clear();
        foreach (var resource in resources)
        {
            SelectedResources.Add(resource);
        }

        SelectedResourceCount = SelectedResources.Count;
    }

    private ResourceItem[] GetBatchTargets(string emptyStatusText)
    {
        var targets = SelectedResources.Count > 0
            ? SelectedResources.ToArray()
            : SelectedResource is null ? [] : [SelectedResource];

        if (targets.Length == 0)
        {
            StatusText = emptyStatusText;
        }

        return targets;
    }

    [RelayCommand]
    private void ToggleAddResourceMenu()
    {
        if (IsScanning)
        {
            return;
        }

        IsAddResourceMenuOpen = !IsAddResourceMenuOpen;
    }

    [RelayCommand]
    private void ToggleSortMenu()
    {
        if (IsScanning)
        {
            return;
        }

        IsSortMenuOpen = !IsSortMenuOpen;
    }

    [RelayCommand]
    private void ToggleExportMenu()
    {
        if (IsScanning)
        {
            return;
        }

        IsExportMenuOpen = !IsExportMenuOpen;
    }

    [RelayCommand]
    private async Task ExportCurrentResourcesToCsvAsync()
    {
        IsExportMenuOpen = false;

        var dialog = new SaveFileDialog
        {
            Title = "导出当前资源列表为 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"LocalResourceExplorer_resources_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            StatusText = "已取消 CSV 导出。";
            return;
        }

        try
        {
            await exportBackupService.ExportResourcesToCsvAsync(Resources.ToArray(), dialog.FileName);
            StatusText = $"已导出 {Resources.Count} 条登记信息到 CSV。";
        }
        catch (Exception ex)
        {
            StatusText = $"CSV 导出失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportCurrentResourcesToJsonAsync()
    {
        IsExportMenuOpen = false;

        var dialog = new SaveFileDialog
        {
            Title = "导出当前资源列表为 JSON",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"LocalResourceExplorer_resources_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true)
        {
            StatusText = "已取消 JSON 导出。";
            return;
        }

        try
        {
            await exportBackupService.ExportResourcesToJsonAsync(Resources.ToArray(), dialog.FileName);
            StatusText = $"已导出 {Resources.Count} 条登记信息到 JSON。";
        }
        catch (Exception ex)
        {
            StatusText = $"JSON 导出失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        IsExportMenuOpen = false;

        var dialog = new SaveFileDialog
        {
            Title = "备份 SQLite 数据库",
            Filter = "SQLite 数据库备份 (*.db)|*.db|所有文件 (*.*)|*.*",
            FileName = $"LocalResourceExplorer_library_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            AddExtension = true,
            DefaultExt = ".db"
        };

        if (dialog.ShowDialog() != true)
        {
            StatusText = "已取消数据库备份。";
            return;
        }

        try
        {
            await exportBackupService.BackupDatabaseAsync(dialog.FileName);
            StatusText = $"数据库已备份到：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"数据库备份失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreDatabaseFromBackupAsync()
    {
        IsExportMenuOpen = false;

        var dialog = new OpenFileDialog
        {
            Title = "选择 SQLite 数据库备份",
            Filter = "SQLite 数据库备份 (*.db)|*.db|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            StatusText = "已取消数据库恢复。";
            return;
        }

        var result = MessageBox.Show(
            "恢复备份会覆盖当前资源库数据库。\n当前库内记录、集合、标签和设置都会被备份文件替换。\n\n确定继续吗？",
            "恢复数据库备份",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            StatusText = "已取消数据库恢复。";
            return;
        }

        try
        {
            await exportBackupService.RestoreDatabaseAsync(dialog.FileName);
            SelectedCollection = null;
            SelectedTag = null;
            currentViewFilter = ViewAll;
            ViewTitle = AllResourcesViewTitle;
            await RefreshCollectionsAsync();
            await RefreshTagsAsync();
            await RefreshResourcesAsync();
            await LoadSelectedResourceCollectionsAsync(SelectedResource);
            await LoadSelectedResourceTagsAsync(SelectedResource);
            StatusText = "数据库已从备份恢复。";
        }
        catch (Exception ex)
        {
            StatusText = $"数据库恢复失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeButtonText = themeService.ToggleTheme() ? "浅色" : "深色";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsView
        {
            Owner = Application.Current.MainWindow
        };

        window.ShowDialog();
        ThemeButtonText = themeService.IsDarkTheme ? "浅色" : "深色";
    }

    [RelayCommand]
    private async Task RequestAiSuggestionsAsync()
    {
        if (IsAiBusy)
        {
            return;
        }

        var targetResources = SelectedResources.Count > 0
            ? SelectedResources.ToArray()
            : SelectedResource is null ? [] : [SelectedResource];

        if (targetResources.Length == 0)
        {
            StatusText = "请先选择需要 AI 辅助整理的资源。";
            return;
        }

        try
        {
            IsAiBusy = true;
            StatusText = "正在请求 AI 整理建议...";

            var existingCollections = (await collectionRepository.GetAllAsync())
                .Select(collection => collection.Name)
                .ToArray();
            var existingTags = (await tagRepository.GetAllAsync())
                .Select(tag => tag.Name)
                .ToArray();

            var suggestions = await aiService.SuggestOrganizationAsync(
                targetResources,
                existingCollections,
                existingTags,
                includeNotes: true);

            var resourcesById = targetResources.ToDictionary(resource => resource.Id);
            foreach (var suggestion in suggestions)
            {
                if (resourcesById.TryGetValue(suggestion.ResourceId, out var resource) &&
                    string.IsNullOrWhiteSpace(suggestion.ResourceName))
                {
                    suggestion.ResourceName = resource.OriginalName;
                }
            }

            var window = new AiSuggestionsView(
                suggestions,
                resourceRepository,
                collectionRepository,
                tagRepository)
            {
                Owner = Application.Current.MainWindow
            };

            window.ShowDialog();
            if (window.HasApplied)
            {
                await RefreshCollectionsAsync();
                await RefreshTagsAsync();
                await RefreshResourcesAsync();
                await LoadSelectedResourceCollectionsAsync(SelectedResource);
                await LoadSelectedResourceTagsAsync(SelectedResource);
                StatusText = "AI 选中建议已应用。";
            }
            else
            {
                StatusText = $"AI 已返回 {suggestions.Count} 条建议，未应用到数据库。";
            }
        }
        catch (AiServiceException ex)
        {
            StatusText = ex.Message;
            MessageBox.Show(ex.Message, "AI 调用失败", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"AI 调用失败：{ex.Message}";
            MessageBox.Show($"AI 调用失败：{ex.Message}", "AI 调用失败", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    [RelayCommand]
    private async Task SortByAsync(string? sortBy)
    {
        IsSortMenuOpen = false;

        var requestedSortBy = string.IsNullOrWhiteSpace(sortBy) ? "imported_at" : sortBy;
        if (string.Equals(currentSortBy, requestedSortBy, StringComparison.OrdinalIgnoreCase))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            currentSortBy = requestedSortBy;
            SortDescending = true;
        }

        SortButtonText = $"排序：{GetSortDisplayName(currentSortBy)} {(SortDescending ? "↓" : "↑")}";
        await RefreshResourcesAsync();
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        IsAddResourceMenuOpen = false;

        var dialog = new OpenFileDialog
        {
            Title = "选择要登记的文件",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            StatusText = "已取消添加文件。";
            return;
        }

        await ScanAndImportFilesAsync(dialog.FileNames);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        IsAddResourceMenuOpen = false;

        var dialog = new OpenFolderDialog
        {
            Title = "选择要扫描的文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            StatusText = "已取消添加文件夹。";
            return;
        }

        await ScanAndImportFolderAsync(dialog.FolderName);
    }

    private async Task InitializeAndLoadResourcesAsync()
    {
        try
        {
            await databaseService.InitializeAsync();
            await ApplySavedThemeAsync();
            await RefreshCollectionsAsync();
            await RefreshTagsAsync();
            await RefreshResourcesAsync();
            StatusText = Resources.Count == 0 ? "扫描状态：空闲" : $"已加载资源：{Resources.Count}";
            _ = CheckMissingFilesInBackgroundAsync();
        }
        catch (Exception ex)
        {
            AppLog.DatabaseError(ex, "Application database initialization or load failed", databaseService.DatabasePath);
            StatusText = $"数据库初始化失败：{ex.Message}";
        }
    }

    private async Task ApplySavedThemeAsync()
    {
        var theme = await appSettingsRepository.GetAsync("theme");
        var useDarkTheme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        themeService.ApplyTheme(useDarkTheme);
        ThemeButtonText = useDarkTheme ? "浅色" : "深色";
    }

    private async Task CheckMissingFilesInBackgroundAsync()
    {
        try
        {
            StatusText = "正在后台检查丢失文件...";
            var result = await missingFileChecker.CheckAsync();
            await RefreshResourcesAsync();
            StatusText = result.MissingCount == 0
                ? $"文件存在性检查完成：{result.CheckedCount} 个资源可用"
                : $"文件存在性检查完成：{result.MissingCount} 个资源可能已丢失";
        }
        catch (Exception ex)
        {
            StatusText = $"文件存在性检查失败：{ex.Message}";
        }
    }

    private async Task ScanAndImportFilesAsync(IReadOnlyCollection<string> filePaths)
    {
        await RunScanAndImportAsync(
            progressPrefix: "添加文件",
            scanAction: token => resourceScanner.ScanFilesAsync(filePaths, CreateProgress(), token));
    }

    private async Task ScanAndImportFolderAsync(string folderPath)
    {
        await RunScanAndImportAsync(
            progressPrefix: "扫描文件夹",
            scanAction: token => resourceScanner.ScanFolderAsync(folderPath, recursive: true, CreateProgress(), token));
    }

    private async Task RunScanAndImportAsync(
        string progressPrefix,
        Func<CancellationToken, Task<IReadOnlyList<ResourceItem>>> scanAction)
    {
        if (IsScanning)
        {
            return;
        }

        scanCancellationTokenSource?.Dispose();
        scanCancellationTokenSource = new CancellationTokenSource();
        var token = scanCancellationTokenSource.Token;
        skippedCount = 0;

        try
        {
            IsScanning = true;
            StatusText = $"{progressPrefix}：准备中...";

            await databaseService.InitializeAsync(token);

            var scannedResources = await scanAction(token);
            var insertedCount = 0;

            foreach (var resource in scannedResources)
            {
                token.ThrowIfCancellationRequested();
                insertedCount += await resourceRepository.InsertOrIgnoreAsync(resource);
            }

            await RefreshResourcesAsync();
            await RefreshTagsAsync();

            var duplicateCount = scannedResources.Count - insertedCount;
            StatusText = $"{progressPrefix}完成：新增 {insertedCount}，重复 {duplicateCount}，跳过 {skippedCount}";

            _ = UpdateMediaDurationsInBackgroundAsync(scannedResources);
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{progressPrefix}已取消。";
        }
        catch (Exception ex)
        {
            StatusText = $"{progressPrefix}失败：{ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task UpdateMediaDurationsInBackgroundAsync(IReadOnlyList<ResourceItem> scannedResources)
    {
        var mediaResources = scannedResources
            .Where(resource => mediaInfoService.IsSupported(resource.Path))
            .ToArray();

        if (mediaResources.Length == 0)
        {
            return;
        }

        try
        {
            StatusText = $"正在后台读取媒体时长：0/{mediaResources.Length}";
            var processedCount = 0;
            var updatedCount = 0;

            foreach (var resource in mediaResources)
            {
                var durationMs = await mediaInfoService.TryReadDurationMsAsync(resource.Path);
                processedCount++;
                if (durationMs is not null)
                {
                    updatedCount += await resourceRepository.UpdateDurationByPathAsync(resource.Path, durationMs.Value);
                }

                StatusText = $"正在后台读取媒体时长：{processedCount}/{mediaResources.Length}";
            }

            if (updatedCount > 0)
            {
                await RefreshResourcesAsync();
            }

            StatusText = $"媒体时长读取完成：更新 {updatedCount} 个";
        }
        catch (Exception ex)
        {
            StatusText = $"媒体时长读取已跳过：{ex.Message}";
        }
    }

    private Progress<ResourceScanProgress> CreateProgress()
    {
        return new Progress<ResourceScanProgress>(progress =>
        {
            skippedCount = Math.Max(skippedCount, progress.SkippedCount);
            StatusText = progress.HasError
                ? $"扫描中：已发现 {progress.ScannedCount}，已跳过 {skippedCount}"
                : $"扫描中：已发现 {progress.ScannedCount} 个文件";
        });
    }

    private async Task RefreshResourcesAsync()
    {
        var resources = await resourceRepository.SearchAsync(
            SearchText,
            currentSortBy,
            SortDescending,
            SelectedCollection?.Id,
            SelectedTag?.Id,
            favoritesOnly: currentViewFilter == ViewFavorites,
            unorganizedOnly: currentViewFilter == ViewUnorganized,
            recentOpenedOnly: currentViewFilter == ViewRecentOpened);

        Resources.Clear();
        foreach (var resource in resources)
        {
            Resources.Add(resource);
        }

        SelectedResource = Resources.FirstOrDefault();
    }

    private async Task RefreshCollectionsAsync()
    {
        var collections = await collectionRepository.GetAllAsync();

        Collections.Clear();
        foreach (var collection in collections)
        {
            Collections.Add(collection);
        }
    }

    private async Task RefreshTagsAsync()
    {
        var tags = await tagRepository.GetAllWithResourceCountsAsync();

        Tags.Clear();
        foreach (var tag in tags)
        {
            Tags.Add(tag);
        }
    }

    private async Task LoadSelectedResourceCollectionsAsync(ResourceItem? resource)
    {
        SelectedResourceCollections.Clear();
        if (resource is null || resource.Id <= 0)
        {
            return;
        }

        try
        {
            var collections = await collectionRepository.GetByResourceAsync(resource.Id);
            foreach (var collection in collections)
            {
                SelectedResourceCollections.Add(collection);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"加载资源所属集合失败：{ex.Message}";
        }
    }

    private async Task LoadSelectedResourceTagsAsync(ResourceItem? resource)
    {
        SelectedResourceTags.Clear();
        if (resource is null || resource.Id <= 0)
        {
            return;
        }

        try
        {
            var tags = await tagRepository.GetByResourceAsync(resource.Id);
            foreach (var tag in tags)
            {
                SelectedResourceTags.Add(tag);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"加载资源标签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ShowAllResourcesAsync()
    {
        SelectedCollection = null;
        SelectedTag = null;
        currentViewFilter = ViewAll;
        ViewTitle = AllResourcesViewTitle;
        await RefreshResourcesAsync();
        StatusText = $"已加载全部资源：{Resources.Count}";
    }

    [RelayCommand]
    private async Task ShowFavoritesAsync()
    {
        SelectedCollection = null;
        SelectedTag = null;
        currentViewFilter = ViewFavorites;
        ViewTitle = "星标";
        await RefreshResourcesAsync();
        StatusText = $"星标资源：{Resources.Count}";
    }

    [RelayCommand]
    private async Task ShowUnorganizedAsync()
    {
        SelectedCollection = null;
        SelectedTag = null;
        currentViewFilter = ViewUnorganized;
        ViewTitle = "未整理";
        await RefreshResourcesAsync();
        StatusText = $"未整理资源：{Resources.Count}";
    }

    [RelayCommand]
    private async Task ShowRecentAddedAsync()
    {
        SelectedCollection = null;
        SelectedTag = null;
        currentViewFilter = ViewRecentAdded;
        currentSortBy = "imported_at";
        SortDescending = true;
        SortButtonText = "排序：导入时间 ↓";
        ViewTitle = "最近添加";
        await RefreshResourcesAsync();
        StatusText = $"最近添加：{Resources.Count}";
    }

    [RelayCommand]
    private async Task ShowRecentOpenedAsync()
    {
        SelectedCollection = null;
        SelectedTag = null;
        currentViewFilter = ViewRecentOpened;
        currentSortBy = "last_opened_at";
        SortDescending = true;
        SortButtonText = "排序：上次打开时间 ↓";
        ViewTitle = "最近打开";
        await RefreshResourcesAsync();
        StatusText = $"最近打开：{Resources.Count}";
    }

    [RelayCommand]
    private async Task SelectCollectionAsync(CollectionModel? collection)
    {
        if (collection is null)
        {
            return;
        }

        SelectedCollection = collection;
        SelectedTag = null;
        currentViewFilter = ViewAll;
        ViewTitle = $"集合：{collection.Name}";
        await RefreshResourcesAsync();
        StatusText = $"集合“{collection.Name}”：{Resources.Count} 个资源";
    }

    [RelayCommand]
    private async Task SelectTagAsync(TagSummary? tag)
    {
        if (tag is null)
        {
            return;
        }

        SelectedTag = tag;
        SelectedCollection = null;
        currentViewFilter = ViewAll;
        ViewTitle = $"标签：{tag.Name}";
        await RefreshResourcesAsync();
        StatusText = $"标签“{tag.Name}”：{Resources.Count} 个资源";
    }

    [RelayCommand]
    private async Task CreateCollectionAsync()
    {
        var name = PromptText("新建集合", "请输入集合名称：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            await collectionRepository.CreateAsync(name.Trim());
            await RefreshCollectionsAsync();
            StatusText = $"已创建集合：{name.Trim()}";
        }
        catch (Exception ex)
        {
            StatusText = $"创建集合失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameCollectionAsync(CollectionModel? collection)
    {
        if (collection is null)
        {
            return;
        }

        var name = PromptText("重命名集合", "请输入新的集合名称：", collection.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), collection.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await collectionRepository.RenameAsync(collection.Id, name.Trim());
            await RefreshCollectionsAsync();
            if (SelectedCollection?.Id == collection.Id)
            {
                SelectedCollection = Collections.FirstOrDefault(item => item.Id == collection.Id);
                ViewTitle = SelectedCollection is null ? AllResourcesViewTitle : $"集合：{SelectedCollection.Name}";
            }

            await RefreshResourcesAsync();
            StatusText = $"已重命名集合：{name.Trim()}";
        }
        catch (Exception ex)
        {
            StatusText = $"重命名集合失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync(CollectionModel? collection)
    {
        if (collection is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定删除集合“{collection.Name}”吗？\n这只会删除库内集合和关联关系，不会删除任何资源记录或真实文件。",
            "删除集合",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await collectionRepository.DeleteAsync(collection.Id);
            if (SelectedCollection?.Id == collection.Id)
            {
                SelectedCollection = null;
                ViewTitle = AllResourcesViewTitle;
            }

            await RefreshCollectionsAsync();
            await RefreshResourcesAsync();
            await LoadSelectedResourceCollectionsAsync(SelectedResource);
            StatusText = $"已删除集合：{collection.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"删除集合失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddResourceToCollectionAsync(ResourceItem? resource)
    {
        if (resource is null)
        {
            StatusText = "请选择一个资源。";
            return;
        }

        if (Collections.Count == 0)
        {
            StatusText = "请先创建集合。";
            return;
        }

        var collection = PromptCollection("加入集合", "请选择要加入的集合：");
        if (collection is null)
        {
            return;
        }

        try
        {
            await collectionRepository.AddResourceAsync(resource.Id, collection.Id);
            await LoadSelectedResourceCollectionsAsync(resource);
            if (SelectedCollection is not null)
            {
                await RefreshResourcesAsync();
            }

            StatusText = $"已将“{resource.OriginalName}”加入集合“{collection.Name}”";
        }
        catch (Exception ex)
        {
            StatusText = $"加入集合失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedResourceFromCollectionAsync(CollectionModel? collection)
    {
        if (SelectedResource is null || collection is null)
        {
            StatusText = "请选择资源和集合。";
            return;
        }

        try
        {
            await collectionRepository.RemoveResourceAsync(SelectedResource.Id, collection.Id);
            await LoadSelectedResourceCollectionsAsync(SelectedResource);
            if (SelectedCollection?.Id == collection.Id)
            {
                await RefreshResourcesAsync();
            }

            StatusText = $"已从集合“{collection.Name}”移出当前资源";
        }
        catch (Exception ex)
        {
            StatusText = $"移出集合失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BatchAddSelectedResourcesToCollectionAsync()
    {
        var targets = GetBatchTargets("请先选择要加入集合的资源。");
        if (targets.Length == 0)
        {
            return;
        }

        await RefreshCollectionsAsync();
        if (Collections.Count == 0)
        {
            StatusText = "请先创建集合。";
            return;
        }

        var collection = PromptCollection("批量加入集合", "请选择要加入的集合：");
        if (collection is null)
        {
            return;
        }

        try
        {
            var changedCount = 0;
            foreach (var resource in targets)
            {
                changedCount += await collectionRepository.AddResourceAsync(resource.Id, collection.Id);
            }

            await RefreshAfterBatchOperationAsync();
            StatusText = $"已将 {targets.Length} 个资源加入集合“{collection.Name}”，新增关联 {changedCount} 个。";
        }
        catch (Exception ex)
        {
            StatusText = $"批量加入集合失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BatchRemoveSelectedResourcesFromCollectionAsync()
    {
        var targets = GetBatchTargets("请先选择要移出集合的资源。");
        if (targets.Length == 0)
        {
            return;
        }

        await RefreshCollectionsAsync();
        if (Collections.Count == 0)
        {
            StatusText = "当前没有集合。";
            return;
        }

        var collection = PromptCollection("批量移出集合", "请选择要移出的集合：");
        if (collection is null)
        {
            return;
        }

        try
        {
            var changedCount = 0;
            foreach (var resource in targets)
            {
                changedCount += await collectionRepository.RemoveResourceAsync(resource.Id, collection.Id);
            }

            await RefreshAfterBatchOperationAsync();
            StatusText = $"已将 {targets.Length} 个资源从集合“{collection.Name}”移出，移除关联 {changedCount} 个。";
        }
        catch (Exception ex)
        {
            StatusText = $"批量移出集合失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateTagAsync()
    {
        var name = PromptText("新建标签", "请输入标签名称：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var color = PromptText("标签颜色", "请输入标签颜色，例如 #EEF6FF：", "#EEF6FF");
        if (string.IsNullOrWhiteSpace(color))
        {
            color = "#EEF6FF";
        }

        try
        {
            await tagRepository.CreateAsync(name.Trim(), color.Trim());
            await RefreshTagsAsync();
            StatusText = $"已创建标签：{name.Trim()}";
        }
        catch (Exception ex)
        {
            StatusText = $"创建标签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteTagAsync(TagSummary? tag)
    {
        if (tag is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定删除标签“{tag.Name}”吗？\n这只会删除库内标签和关联关系，不会删除任何资源记录或真实文件。",
            "删除标签",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await tagRepository.DeleteAsync(tag.Id);
            if (SelectedTag?.Id == tag.Id)
            {
                SelectedTag = null;
                ViewTitle = AllResourcesViewTitle;
            }

            await RefreshTagsAsync();
            await RefreshResourcesAsync();
            await LoadSelectedResourceTagsAsync(SelectedResource);
            StatusText = $"已删除标签：{tag.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"删除标签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ChangeTagColorAsync(TagSummary? tag)
    {
        if (tag is null)
        {
            return;
        }

        var color = PromptText("修改标签颜色", "请输入标签颜色，例如 #EEF6FF：", tag.Color);
        if (string.IsNullOrWhiteSpace(color))
        {
            return;
        }

        try
        {
            await tagRepository.UpdateColorAsync(tag.Id, color.Trim());
            await RefreshTagsAsync();
            await LoadSelectedResourceTagsAsync(SelectedResource);
            StatusText = $"已修改标签颜色：{tag.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"修改标签颜色失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddTagToResourceAsync(ResourceItem? resource)
    {
        if (resource is null)
        {
            StatusText = "请选择一个资源。";
            return;
        }

        await AddTagToResourcesAsync([resource]);
    }

    [RelayCommand]
    private async Task AddTagToSelectedResourcesAsync()
    {
        var targets = SelectedResources.Count > 0
            ? SelectedResources.ToArray()
            : SelectedResource is null ? [] : [SelectedResource];

        await AddTagToResourcesAsync(targets);
    }

    private async Task AddTagToResourcesAsync(IReadOnlyCollection<ResourceItem> resources)
    {
        if (resources.Count == 0)
        {
            StatusText = "请选择要打标签的资源。";
            return;
        }

        var tagName = PromptText("添加标签", "请输入标签名称：");
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return;
        }

        var tag = Tags.FirstOrDefault(item =>
            string.Equals(item.Name, tagName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            var result = MessageBox.Show(
                $"标签“{tagName.Trim()}”不存在，是否创建后再应用？",
                "创建标签",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                StatusText = "未创建标签，未应用任何修改。";
                return;
            }

            var color = PromptText("标签颜色", "请输入标签颜色，例如 #EEF6FF：", "#EEF6FF");
            var tagId = await tagRepository.CreateAsync(
                tagName.Trim(),
                string.IsNullOrWhiteSpace(color) ? "#EEF6FF" : color.Trim());
            await RefreshTagsAsync();
            tag = Tags.First(item => item.Id == tagId);
        }

        try
        {
            var changedCount = 0;
            foreach (var resource in resources)
            {
                changedCount += await tagRepository.AddResourceAsync(resource.Id, tag.Id);
            }

            await RefreshAfterBatchOperationAsync();

            StatusText = $"已给 {resources.Count} 个资源添加标签“{tag.Name}”，新增关联 {changedCount} 个";
        }
        catch (Exception ex)
        {
            StatusText = $"添加标签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedResourceTagAsync(Tag? tag)
    {
        if (SelectedResource is null || tag is null)
        {
            StatusText = "请选择资源和标签。";
            return;
        }

        try
        {
            await tagRepository.RemoveResourceAsync(SelectedResource.Id, tag.Id);
            await RefreshTagsAsync();
            await LoadSelectedResourceTagsAsync(SelectedResource);
            if (SelectedTag?.Id == tag.Id)
            {
                await RefreshResourcesAsync();
            }

            StatusText = $"已从当前资源移除标签“{tag.Name}”";
        }
        catch (Exception ex)
        {
            StatusText = $"移除标签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BatchRemoveTagFromSelectedResourcesAsync()
    {
        var targets = GetBatchTargets("请先选择要移除标签的资源。");
        if (targets.Length == 0)
        {
            return;
        }

        await RefreshTagsAsync();
        if (Tags.Count == 0)
        {
            StatusText = "当前没有标签。";
            return;
        }

        var tagName = PromptText("批量移除标签", "请输入已有标签名称：");
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return;
        }

        var tag = Tags.FirstOrDefault(item =>
            string.Equals(item.Name, tagName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            StatusText = $"未找到标签：{tagName.Trim()}";
            return;
        }

        try
        {
            var changedCount = 0;
            foreach (var resource in targets)
            {
                changedCount += await tagRepository.RemoveResourceAsync(resource.Id, tag.Id);
            }

            await RefreshAfterBatchOperationAsync();
            StatusText = $"已从 {targets.Length} 个资源移除标签“{tag.Name}”，移除关联 {changedCount} 个。";
        }
        catch (Exception ex)
        {
            StatusText = $"批量移除标签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BatchSetFavoriteAsync(string? value)
    {
        var targets = GetBatchTargets("请先选择要更新星标的资源。");
        if (targets.Length == 0)
        {
            return;
        }

        var isFavorite = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        try
        {
            foreach (var resource in targets)
            {
                await resourceRepository.UpdateFavoriteAsync(resource.Id, isFavorite);
            }

            await RefreshAfterBatchOperationAsync();
            StatusText = isFavorite
                ? $"已星标 {targets.Length} 个资源。"
                : $"已取消星标 {targets.Length} 个资源。";
        }
        catch (Exception ex)
        {
            StatusText = $"批量更新星标失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BatchDeleteSelectedRecordsAsync()
    {
        var targets = GetBatchTargets("请先选择要从库中移除的资源记录。");
        if (targets.Length == 0)
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定从库中移除选中的 {targets.Length} 条资源记录吗？\n这只会删除库内记录，不会删除真实文件。",
            "从库中移除记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            StatusText = "已取消从库中移除记录。";
            return;
        }

        try
        {
            var changedCount = 0;
            foreach (var resource in targets)
            {
                changedCount += await resourceRepository.DeleteRecordAsync(resource.Id);
            }

            await RefreshAfterBatchOperationAsync();
            StatusText = $"已从库中移除 {changedCount} 条资源记录，未删除真实文件。";
        }
        catch (Exception ex)
        {
            StatusText = $"批量移除库内记录失败：{ex.Message}";
        }
    }

    private async Task RefreshAfterBatchOperationAsync()
    {
        await RefreshCollectionsAsync();
        await RefreshTagsAsync();
        await RefreshResourcesAsync();
        await LoadSelectedResourceCollectionsAsync(SelectedResource);
        await LoadSelectedResourceTagsAsync(SelectedResource);
    }

    private async Task RefreshResourcesWithDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            StatusText = "正在搜索...";
            await RefreshResourcesAsync();
            StatusText = string.IsNullOrWhiteSpace(SearchText)
                ? $"已加载资源：{Resources.Count}"
                : $"搜索完成：{Resources.Count} 个结果";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.DatabaseError(ex, "Resource search failed", databaseService.DatabasePath);
            StatusText = $"搜索失败：{ex.Message}";
        }
    }

    private static string GetSortDisplayName(string sortBy)
    {
        return sortBy.ToLowerInvariant() switch
        {
            "title" => "名称",
            "size_bytes" => "文件大小",
            "modified_at" => "修改时间",
            "imported_at" => "导入时间",
            "duration_ms" => "音视频时长",
            "last_opened_at" => "上次打开时间",
            _ => "导入时间"
        };
    }

    private static string PromptText(string title, string prompt, string defaultValue = "")
    {
        var dialog = new TextPromptDialog(title, prompt, defaultValue)
        {
            Owner = Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.ResponseText : string.Empty;
    }

    private CollectionModel? PromptCollection(string title, string prompt)
    {
        var dialog = new CollectionPickerDialog(Collections, title, prompt)
        {
            Owner = Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.SelectedCollection : null;
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedResource is null)
        {
            StatusText = "请选择一个资源。";
            return;
        }

        try
        {
            await resourceRepository.UpdateNoteAsync(SelectedResource.Id, SelectedResource.Note ?? string.Empty);
            StatusText = $"已保存备注：{SelectedResource.OriginalName}";
            await RefreshResourcesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"保存备注失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedResource is null)
        {
            StatusText = "请选择一个资源。";
            return;
        }

        try
        {
            await resourceRepository.UpdateFavoriteAsync(SelectedResource.Id, SelectedResource.IsFavorite);
            StatusText = SelectedResource.IsFavorite
                ? $"已星标：{SelectedResource.OriginalName}"
                : $"已取消星标：{SelectedResource.OriginalName}";
            await RefreshResourcesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"更新星标失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync(ResourceItem? resource)
    {
        if (resource is null)
        {
            StatusText = "请选择一个资源。";
            return;
        }

        var openResult = fileOpenService.OpenFile(resource.Path);
        if (openResult.IsMissing)
        {
            await resourceRepository.MarkMissingAsync(resource.Id, true);
            resource.IsMissing = true;
            StatusText = "文件不存在或已移动";
            MessageBox.Show("文件不存在或已移动。", "文件丢失", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshResourcesAsync();
            return;
        }

        if (!openResult.IsSuccess)
        {
            StatusText = openResult.ErrorMessage ?? "无法打开文件。";
            return;
        }

        try
        {
            var openedAt = DateTime.Now;
            if (resource.IsMissing)
            {
                await resourceRepository.MarkMissingAsync(resource.Id, false);
                resource.IsMissing = false;
            }

            await resourceRepository.UpdateLastOpenedAsync(resource.Id, openedAt);
            resource.LastOpenedAt = openedAt;
            StatusText = $"已打开文件：{resource.OriginalName}";
            await RefreshResourcesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"文件已打开，但更新打开时间失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenContainingFolder(ResourceItem? resource)
    {
        if (resource is null)
        {
            StatusText = "请选择一个资源。";
            return;
        }

        var error = fileOpenService.OpenContainingFolder(resource.Path);
        StatusText = error ?? $"已打开所在文件夹：{resource.OriginalName}";
    }

    [RelayCommand]
    private void CopyPath(ResourceItem? resource)
    {
        if (resource is null)
        {
            StatusText = "请选择一个资源。";
            return;
        }

        var error = fileOpenService.CopyPathToClipboard(resource.Path);
        StatusText = error ?? $"已复制路径：{resource.Path}";
    }
}
