namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentMessageCatalogPersistenceRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void CatalogMutationsAreWriteThroughAndRollbackOnPersistenceFailure()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "DevelopmentMessageCatalogControl.cs");

        Assert.Contains("PersistMutation(previous, _items.Count - 1", source, StringComparison.Ordinal);
        Assert.Contains("PersistMutation(previous, index", source, StringComparison.Ordinal);
        Assert.Contains("PersistMutation(previous, Math.Max(0, index - 1)", source, StringComparison.Ordinal);
        Assert.Contains("PersistMutation(previous, target", source, StringComparison.Ordinal);
        Assert.Contains("PersistCatalogCore();", source, StringComparison.Ordinal);
        Assert.Contains("_items = previous;", source, StringComparison.Ordinal);
        Assert.Contains("Save failed — change rolled back.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Added — save catalog to persist.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Updated — save catalog to persist.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeStartReloadsTheCanonicalPersistedCatalogBeforeZeroMessageValidation()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "DevelopmentTaskEngine.cs");
        var load = source.IndexOf("var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);", StringComparison.Ordinal);
        var validate = source.IndexOf("if (messages.Count == 0) throw new InvalidOperationException(\"No development task messages are configured.\");", StringComparison.Ordinal);

        Assert.True(load >= 0, "StartAsync must reload the canonical persisted catalog.");
        Assert.True(validate > load, "Zero-message validation must occur after the catalog is reloaded.");
        Assert.Contains("_messagesPath = messagesPath ?? Path.Combine(AppContext.BaseDirectory, \"data\", \"development-task-messages.json\")", source, StringComparison.Ordinal);
    }
}
