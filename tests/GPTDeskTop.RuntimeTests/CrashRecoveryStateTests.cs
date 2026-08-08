using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryStateTests
{
    [Fact]
    public async Task FirstStartupInitializesCleanStateWithoutCountingCrash()
    {
        var root = CreateTempRoot();
        try
        {
            var database = await CreateDatabaseAsync(root);

            var wasUnclean = await CrashRecoveryStateService.PrepareStartupAsync(database);

            Assert.False(wasUnclean);
            Assert.Equal("0", await database.GetSettingAsync("CrashCount"));
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal("0", await database.GetSettingAsync("LastShutdownClean"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task UncleanRestartIncrementsCrashCountAndSchedulesRecovery()
    {
        var root = CreateTempRoot();
        try
        {
            var database = await CreateDatabaseAsync(root);
            await CrashRecoveryStateService.PrepareStartupAsync(database);

            var wasUnclean = await CrashRecoveryStateService.PrepareStartupAsync(database);

            Assert.True(wasUnclean);
            Assert.Equal("1", await database.GetSettingAsync("CrashCount"));
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal("0", await database.GetSettingAsync("LastShutdownClean"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task CleanShutdownPreventsNextStartupFromCountingCrash()
    {
        var root = CreateTempRoot();
        try
        {
            var database = await CreateDatabaseAsync(root);
            await CrashRecoveryStateService.PrepareStartupAsync(database);
            await CrashRecoveryStateService.MarkCleanShutdownAsync(database);

            var wasUnclean = await CrashRecoveryStateService.PrepareStartupAsync(database);

            Assert.False(wasUnclean);
            Assert.Equal("0", await database.GetSettingAsync("CrashCount"));
            Assert.Equal("1", await database.GetSettingAsync("LastShutdownClean"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<LocalDatabase> CreateDatabaseAsync(string root)
    {
        var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
        await database.InitializeAsync();
        return database;
    }

    private static void DeleteTempRoot(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }
}
