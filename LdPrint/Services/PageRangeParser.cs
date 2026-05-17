namespace LdPrint.Services;

/// <summary>
/// Parses Windows-style page range strings ("1,3,5-7" / "all" / "")
/// into a sorted, deduplicated array of 0-based page indices.
///
/// Throws FormatException with the offending token in the message so the
/// UI can surface it to the user. Reverse ranges ("5-3") are silently
/// expanded as "3-5" — easier to be permissive than to argue with users.
/// </summary>
public static class PageRangeParser
{
    public static int[] Parse(string? text, int pageCount)
    {
        if (pageCount <= 0)
            throw new InvalidOperationException("Document has no pages.");

        var trimmed = (text ?? string.Empty).Trim();

        // Empty input or the "all" keyword in any supported language — print
        // every page. Accept Russian synonyms so a localised UI doesn't force
        // the user to type English.
        if (trimmed.Length == 0
            || trimmed.Equals("all", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("все", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("всё", StringComparison.OrdinalIgnoreCase))
        {
            var all = new int[pageCount];
            for (var i = 0; i < pageCount; i++) all[i] = i;
            return all;
        }

        var result = new SortedSet<int>();
        var tokens = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (token.Contains('-'))
            {
                // Range "a-b". Handle negatives later — for now both sides
                // must parse as positive ints.
                var parts = token.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], out var a) ||
                    !int.TryParse(parts[1], out var b))
                {
                    throw new FormatException($"Invalid range '{token}'. Expected 'N-M'.");
                }

                if (a > b) (a, b) = (b, a); // silently fix reverse ranges

                if (a < 1 || b > pageCount)
                    throw new FormatException(
                        $"Range '{token}' is outside 1..{pageCount}.");

                for (var i = a; i <= b; i++)
                    result.Add(i - 1);
            }
            else
            {
                if (!int.TryParse(token, out var n))
                    throw new FormatException($"Invalid page number '{token}'.");

                if (n < 1 || n > pageCount)
                    throw new FormatException(
                        $"Page '{n}' is outside 1..{pageCount}.");

                result.Add(n - 1);
            }
        }

        if (result.Count == 0)
            throw new FormatException("No pages selected.");

        var arr = new int[result.Count];
        result.CopyTo(arr);
        return arr;
    }
}
