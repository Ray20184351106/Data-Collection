namespace Acquisition.Agent.Services;

public sealed class LegacyConfigFileTransaction : IAsyncDisposable
{
    private readonly string _fullPath;
    private readonly string _backupPath;
    private readonly bool _hadOriginal;
    private bool _completed;

    private LegacyConfigFileTransaction(string fullPath, string backupPath, bool hadOriginal)
    {
        _fullPath = fullPath;
        _backupPath = backupPath;
        _hadOriginal = hadOriginal;
    }

    public static async Task<LegacyConfigFileTransaction> StageAsync(
        string path, string content, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var backupPath = fullPath + ".previous";
        var hadOriginal = File.Exists(fullPath);
        if (hadOriginal) File.Copy(fullPath, backupPath, overwrite: true);
        var temporary = fullPath + ".pending";
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, fullPath, overwrite: true);
        return new LegacyConfigFileTransaction(fullPath, backupPath, hadOriginal);
    }

    public Task CommitAsync()
    {
        if (File.Exists(_backupPath)) File.Delete(_backupPath);
        _completed = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_completed) return Task.CompletedTask;
        if (_hadOriginal && File.Exists(_backupPath)) File.Copy(_backupPath, _fullPath, overwrite: true);
        else if (File.Exists(_fullPath)) File.Delete(_fullPath);
        if (File.Exists(_backupPath)) File.Delete(_backupPath);
        _completed = true;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed) await RollbackAsync(CancellationToken.None);
    }
}
