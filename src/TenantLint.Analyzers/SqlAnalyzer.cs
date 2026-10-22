using System.Collections.Immutable;

namespace TenantLint.Analyzers;

internal readonly struct SqlAnalysisResult
{
    public bool HasTenantTableReference { get; }
    public string MatchedTableName { get; }
    public string MatchedEntityName { get; }
    public bool HasTenantColumnInPredicate { get; }
    public string MatchedColumnName { get; }

    public SqlAnalysisResult(
        bool hasTenantTableReference,
        string matchedTableName,
        string matchedEntityName,
        bool hasTenantColumnInPredicate,
        string matchedColumnName)
    {
        HasTenantTableReference = hasTenantTableReference;
        MatchedTableName = matchedTableName;
        MatchedEntityName = matchedEntityName;
        HasTenantColumnInPredicate = hasTenantColumnInPredicate;
        MatchedColumnName = matchedColumnName;
    }

    public static readonly SqlAnalysisResult NoMatch = new(false, "", "", false, "");
}

internal static class SqlAnalyzer
{
    private static readonly ImmutableHashSet<string> PredicateKeywords =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "WHERE", "ON", "AND", "OR");

    private static readonly ImmutableHashSet<string> AllSqlKeywords =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "WHERE", "ON", "AND", "OR", "SELECT", "ORDER", "GROUP", "FROM", "JOIN",
            "HAVING", "SET", "UPDATE", "DELETE", "INSERT", "INTO", "VALUES", "INNER",
            "LEFT", "RIGHT", "OUTER", "CROSS", "FULL");

    public static SqlAnalysisResult Analyze(
        string sql,
        TenantModel model,
        ConfigOptions options)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return SqlAnalysisResult.NoMatch;

        // Check each known table name against the SQL
        foreach (var kvp in model.TableNameToEntity)
        {
            var tableName = kvp.Key;
            if (!ContainsTableNameWord(sql, tableName))
                continue;

            var entityInfo = kvp.Value;
            var entityName = entityInfo.EntityType.Name;

            // Check if a tenant column appears in a predicate clause
            var hasPredicate = HasTenantColumnInPredicate(sql, options.TenantColumnNames);
            var defaultColumn = entityInfo.TenantPropertyName;
            var matchedColumn = hasPredicate
                ? FindFirstTenantColumn(sql, options.TenantColumnNames) ?? defaultColumn
                : defaultColumn;

            return new SqlAnalysisResult(
                hasTenantTableReference: true,
                matchedTableName: tableName,
                matchedEntityName: entityName,
                hasTenantColumnInPredicate: hasPredicate,
                matchedColumnName: matchedColumn);
        }

        return SqlAnalysisResult.NoMatch;
    }

    internal static bool ContainsTableNameWord(string sql, string tableName)
    {
        if (string.IsNullOrEmpty(sql) || string.IsNullOrEmpty(tableName))
            return false;

        var startIndex = 0;
        while (startIndex <= sql.Length - tableName.Length)
        {
            var index = sql.IndexOf(tableName, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var charBefore = index > 0 ? sql[index - 1] : ' ';
            var charAfter = index + tableName.Length < sql.Length
                ? sql[index + tableName.Length]
                : ' ';

            if (!IsIdentifierChar(charBefore) && !IsIdentifierChar(charAfter))
                return true;

            startIndex = index + 1;
        }

        return false;
    }

    internal static bool HasTenantColumnInPredicate(string sql, ImmutableHashSet<string> tenantColumnNames)
    {
        if (string.IsNullOrEmpty(sql) || tenantColumnNames.Count == 0)
            return false;

        foreach (var columnName in tenantColumnNames)
        {
            var startIndex = 0;
            while (startIndex <= sql.Length - columnName.Length)
            {
                var colIndex = sql.IndexOf(columnName, startIndex, StringComparison.OrdinalIgnoreCase);
                if (colIndex < 0)
                    break;

                // Check word boundaries
                var charBefore = colIndex > 0 ? sql[colIndex - 1] : ' ';
                var charAfter = colIndex + columnName.Length < sql.Length
                    ? sql[colIndex + columnName.Length]
                    : ' ';

                if (!IsIdentifierChar(charBefore) && !IsIdentifierChar(charAfter))
                {
                    // Found the column with word boundaries — check preceding keyword
                    var precedingKeyword = FindPrecedingSqlKeyword(sql, colIndex);
                    if (precedingKeyword != null && PredicateKeywords.Contains(precedingKeyword))
                    {
                        // Check for parameter placeholder within ~40 chars after column
                        if (HasParameterPlaceholderNearby(sql, colIndex + columnName.Length, 40))
                            return true;
                    }
                }

                startIndex = colIndex + 1;
            }
        }

        return false;
    }

    private static string? FindFirstTenantColumn(string sql, ImmutableHashSet<string> tenantColumnNames)
    {
        foreach (var columnName in tenantColumnNames)
        {
            if (ContainsTableNameWord(sql, columnName))
                return columnName;
        }

        return null;
    }

    private static string? FindPrecedingSqlKeyword(string sql, int position)
    {
        // Scan backward from position to find the nearest SQL keyword
        var i = position - 1;

        // Skip whitespace and operators before the column name
        while (i >= 0 && (char.IsWhiteSpace(sql[i]) || sql[i] == '(' || sql[i] == '.'))
            i--;

        // Now find the end of the preceding word
        if (i < 0 || IsIdentifierChar(sql[i]) == false)
        {
            // Could be an operator like = before us, skip it
            while (i >= 0 && !char.IsLetterOrDigit(sql[i]) && sql[i] != '_')
                i--;
        }

        if (i < 0)
            return null;

        // Find the word ending at position i
        var wordEnd = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
            i--;
        var wordStart = i + 1;

        if (wordStart >= wordEnd)
            return null;

        var word = sql.Substring(wordStart, wordEnd - wordStart);

        if (AllSqlKeywords.Contains(word))
            return word;

        // The immediately preceding word might not be a keyword (e.g., table alias).
        // Search further back for the nearest keyword.
        return FindPrecedingSqlKeyword(sql, wordStart);
    }

    private static bool HasParameterPlaceholderNearby(string sql, int startPos, int maxDistance)
    {
        var endPos = Math.Min(sql.Length, startPos + maxDistance);

        for (var i = startPos; i < endPos; i++)
        {
            var c = sql[i];

            // @param pattern
            if (c == '@' && i + 1 < endPos && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
                return true;

            // {N} or {name} pattern (interpolation placeholders)
            if (c == '{' && i + 1 < endPos && (char.IsLetterOrDigit(sql[i + 1]) || sql[i + 1] == '_'))
                return true;

            // $N pattern (positional parameter)
            if (c == '$' && i + 1 < endPos && char.IsDigit(sql[i + 1]))
                return true;

            // :param pattern (Oracle/Postgres named param)
            if (c == ':' && i + 1 < endPos && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
            {
                // Don't match :: (Postgres cast operator)
                if (i > 0 && sql[i - 1] == ':')
                    continue;
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
