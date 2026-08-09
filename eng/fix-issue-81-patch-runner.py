from pathlib import Path

path = Path('eng/apply-issue-81.py')
text = path.read_text(encoding='utf-8')
old = '        await using var connection = new SqliteConnection(_connectionString);\n        await connection.OpenAsync(cancellationToken);\n        using var transaction = connection.BeginTransaction();'
new = '        var snapshotConnectionString = new SqliteConnectionStringBuilder(_connectionString)\n            { Cache = SqliteCacheMode.Private }\n            .ToString();\n        await using var connection = new SqliteConnection(snapshotConnectionString);\n        await connection.OpenAsync(cancellationToken);\n        using var transaction = connection.BeginTransaction();'
if text.count(old) != 1:
    raise RuntimeError(f'Issue #81 snapshot connection anchor count={text.count(old)}')
text = text.replace(old, new, 1)
# Strengthen the generated source-contract test to require private-cache snapshot isolation.
needle = '        Assert.Contains("connection.BeginTransaction()", snapshotSource, StringComparison.Ordinal);\n'
replacement = '        Assert.Contains("Cache = SqliteCacheMode.Private", snapshotSource, StringComparison.Ordinal);\n' + needle
if text.count(needle) != 1:
    raise RuntimeError(f'Issue #81 source-contract anchor count={text.count(needle)}')
text = text.replace(needle, replacement, 1)
# The writer in the deterministic test deliberately keeps shared-cache semantics, matching production writers.
path.write_text(text, encoding='utf-8')
print('Issue #81 patch runner hardened for private-cache snapshot reads.')
