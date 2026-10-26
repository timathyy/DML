using System.Text.Json;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace Dml.Launcher.Infrastructure.Instances;

public sealed partial class JsonInstanceService : IInstanceService, IDisposable
{
    private const int DefaultMemoryMegabytes = 2048;
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 720;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly AppPaths paths;
    private readonly ISettingsService settings;
    private readonly ILogger<JsonInstanceService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    public JsonInstanceService(AppPaths paths, ISettingsService settings, ILogger<JsonInstanceService> logger)
    {
        this.paths = paths;
        this.settings = settings;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<GameInstance> CreateAsync(string name, string minecraftVersion, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        string version = string.IsNullOrWhiteSpace(minecraftVersion) ? "未安装" : minecraftVersion.Trim();
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            EnsureUniqueName(instances, name);
            GameInstance instance = NewInstance(name, version) with
            {
                InstallationState = string.Equals(version, "未安装", StringComparison.Ordinal)
                    ? InstanceInstallationState.Ready
                    : InstanceInstallationState.Downloading,
            };
            Directory.CreateDirectory(instance.GameDirectory);
            instances.Add(instance);
            await SaveAsync(instances, cancellationToken);
            LogCreated(logger, instance.Name);
            return instance;
        }
        finally { gate.Release(); }
    }

    public async Task<GameInstance> CopyAsync(Guid sourceId, string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            GameInstance source = Find(instances, sourceId);
            EnsureManagedDirectory(source.GameDirectory);
            EnsureUniqueName(instances, name);
            GameInstance copy = NewInstance(name, source.MinecraftVersion) with
            {
                Loader = source.Loader,
                JavaPath = source.JavaPath,
                MemoryMegabytes = source.MemoryMegabytes,
                Width = source.Width,
                Height = source.Height,
                JvmArguments = source.JvmArguments,
                Fullscreen = source.Fullscreen,
            };
            CopyDirectory(source.GameDirectory, copy.GameDirectory);
            instances.Add(copy);
            await SaveAsync(instances, cancellationToken);
            return copy;
        }
        finally { gate.Release(); }
    }

    public async Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            GameInstance instance = Find(instances, id);
            EnsureUniqueName(instances.Where(item => item.Id != id), name);
            instances[instances.FindIndex(item => item.Id == id)] = instance with { Name = name.Trim() };
            await SaveAsync(instances, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task SetMinecraftVersionAsync(Guid id, string minecraftVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new ArgumentException("Minecraft 版本不能为空。", nameof(minecraftVersion));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            GameInstance instance = Find(instances, id);
            instances[instances.FindIndex(item => item.Id == id)] = instance with
            {
                MinecraftVersion = minecraftVersion.Trim(),
                InstallationState = InstanceInstallationState.Ready,
            };
            await SaveAsync(instances, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task SetInstallationStateAsync(
        Guid id,
        InstanceInstallationState installationState,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            GameInstance instance = Find(instances, id);
            instances[instances.FindIndex(item => item.Id == id)] = instance with { InstallationState = installationState };
            await SaveAsync(instances, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task UpdateLaunchSettingsAsync(
        Guid id,
        string? javaPath,
        int memoryMegabytes,
        int width,
        int height,
        bool fullscreen,
        string? jvmArguments,
        CancellationToken cancellationToken = default)
    {
        if (memoryMegabytes is < 512 or > 131072)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryMegabytes), "内存必须在 512 MB 到 131072 MB 之间。");
        }

        if (width is < 320 or > 16384 || height is < 240 or > 16384)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "窗口尺寸超出允许范围。");
        }

        string? normalizedJavaPath = string.IsNullOrWhiteSpace(javaPath) ? null : Path.GetFullPath(javaPath.Trim());
        if (normalizedJavaPath is not null && !File.Exists(normalizedJavaPath))
        {
            throw new FileNotFoundException("找不到所选 Java 可执行文件。", normalizedJavaPath);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            GameInstance instance = Find(instances, id);
            instances[instances.FindIndex(item => item.Id == id)] = instance with
            {
                JavaPath = normalizedJavaPath,
                MemoryMegabytes = memoryMegabytes,
                Width = width,
                Height = height,
                Fullscreen = fullscreen,
                JvmArguments = string.IsNullOrWhiteSpace(jvmArguments) ? null : jvmArguments.Trim(),
            };
            await SaveAsync(instances, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task MarkLaunchedAsync(Guid id, DateTimeOffset launchedAt, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            GameInstance instance = Find(instances, id);
            instances[instances.FindIndex(item => item.Id == id)] = instance with { LastLaunchedAt = launchedAt };
            await SaveAsync(instances, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            GameInstance instance = Find(instances, id);
            EnsureManagedDirectory(instance.GameDirectory);
            if (Directory.Exists(instance.GameDirectory))
            {
                FileSystem.DeleteDirectory(instance.GameDirectory, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            instances.Remove(instance);
            await SaveAsync(instances, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<GameInstance> ImportAsync(string sourceDirectory, string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException("找不到要导入的游戏目录。");
        string fullSource = Path.GetFullPath(sourceDirectory);
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<GameInstance> instances = await ReadAsync(cancellationToken);
            EnsureUniqueName(instances, name);
            GameInstance instance = NewInstance(name, "已导入");
            CopyDirectory(fullSource, instance.GameDirectory);
            instances.Add(instance);
            await SaveAsync(instances, cancellationToken);
            return instance;
        }
        finally { gate.Release(); }
    }

    private GameInstance NewInstance(string name, string version) => new(Guid.NewGuid(), name.Trim(), version, null, null,
        Path.Combine(InstanceRoot, Guid.NewGuid().ToString("N")), DefaultMemoryMegabytes, DefaultWidth, DefaultHeight, null);

    private string InstanceRoot => Path.Combine(settings.Current.GameRootDirectory, "Instances");

    private async Task<List<GameInstance>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.InstancesFilePath)) return [];
        await using FileStream stream = File.OpenRead(paths.InstancesFilePath);
        return await JsonSerializer.DeserializeAsync<List<GameInstance>>(stream, SerializerOptions, cancellationToken) ?? [];
    }

    private async Task SaveAsync(List<GameInstance> instances, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RoamingDataDirectory);
        string temporaryPath = paths.InstancesFilePath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, instances, SerializerOptions, cancellationToken);
        File.Move(temporaryPath, paths.InstancesFilePath, overwrite: true);
    }

    private void EnsureManagedDirectory(string directory)
    {
        string root = Path.GetFullPath(InstanceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(directory);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("实例目录不在 DML 管理范围内，已拒绝操作。");
    }

    private static GameInstance Find(List<GameInstance> instances, Guid id) =>
        instances.Find(item => item.Id == id) ?? throw new InvalidOperationException("找不到此实例，可能已被删除。");

    private static void EnsureUniqueName(IEnumerable<GameInstance> instances, string name)
    {
        if (instances.Any(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已存在同名实例，请使用其他名称。");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("实例名称不能为空、不能超过 80 个字符，也不能包含文件名非法字符。", nameof(name));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", System.IO.SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: false);
    }

    public void Dispose() => gate.Dispose();

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "创建了实例 {name}。")]
    private static partial void LogCreated(ILogger logger, string name);
}
