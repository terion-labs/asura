using System.Globalization;
using System.Text;
using Asura.App.Controls;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// The bounded CSV interchange used by the database grid. Parsing is kept
/// separate from row staging so malformed input cannot leave half an import in
/// the editor. Every refusal is written for the person importing the file
/// and is marked readable, so the panel shows it as written.
/// </summary>
internal static class DatabaseGridCsv
{
    public const int MaximumCharacters = 16 * 1024 * 1024;
    public const int MaximumRows = 5000;
    public const int MaximumColumns = 4096;
    public const int MaximumHeaderCharacters = 4096;
    public const int MaximumStagedCells = 100_000;

    public static void ValidateStagingSize(int rowCount, int tableColumnCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(tableColumnCount, 1);
        if ((long)rowCount * tableColumnCount > MaximumStagedCells)
        {
            throw Refused(
                $"The import would change more than {MaximumStagedCells.ToString("N0", CultureInfo.InvariantCulture)} cells. "
                + "Import fewer rows at a time.");
        }
    }

    public static DatabaseGridCsvDocument Parse(
        string text,
        int maximumColumns = MaximumColumns)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumColumns is < 1 or > MaximumColumns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumColumns),
                $"CSV column limits must be between 1 and {MaximumColumns}.");
        }

        if (text.Length > MaximumCharacters)
        {
            throw Refused("The CSV file is too large to import. Import a file of up to 16 MB, or split it into smaller files.");
        }

        var rows = ParseRows(text.TrimStart('\uFEFF'), maximumColumns);
        if (rows.Count == 0)
        {
            throw Refused("The CSV file is empty. Choose a file whose first row names the columns.");
        }

        if (rows.Count > MaximumRows + 1)
        {
            throw TooManyRows();
        }

        var headers = rows[0].ToArray();
        if (headers.Length == 0 || headers.Any(header => header.Length == 0))
        {
            throw Refused("Every column needs a name in the first row of the CSV file. Add the missing names, then import again.");
        }

        var duplicates = headers
            .GroupBy(header => header, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            var shown = string.Join(
                ", ",
                duplicates.Take(5).Select(header => $"“{DescribeHeader(header)}”"));
            var remainder = duplicates.Length > 5
                ? $" and {CountText.Of(duplicates.Length - 5, "other", "others")}"
                : string.Empty;
            throw Refused(
                $"Column names in a CSV file must be unique, but {shown}{remainder} "
                + (duplicates.Length == 1 ? "appears" : "appear")
                + " more than once. Rename the repeated columns, then import again.");
        }

        var data = new List<IReadOnlyList<string>>(Math.Max(0, rows.Count - 1));
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count != headers.Length)
            {
                throw Refused(
                    $"Row {rowIndex + 1:N0} of the CSV file has {CountText.Of(row.Count, "value", "values")}, but the header has {CountText.Of(headers.Length, "column", "columns")}. Fix that row, then import again.");
            }

            data.Add(row);
        }

        return new DatabaseGridCsvDocument(headers, data);
    }

    internal static string DescribeHeader(string header)
    {
        const int maximumShownCharacters = 128;
        var oneLine = header
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        return oneLine.Length <= maximumShownCharacters
            ? oneLine
            : oneLine[..(maximumShownCharacters - 1)] + "…";
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseRows(
        string text,
        int maximumColumns)
    {
        var rows = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var afterQuote = false;
        var fieldStarted = false;
        var rowStarted = false;

        void EndField()
        {
            if (fields.Count >= maximumColumns)
            {
                throw Refused(
                    $"A row of the CSV file has more than {CountText.Of(maximumColumns, "column", "columns")}. Remove the extra columns, then import again.");
            }

            if (rows.Count == 0 && field.Length > MaximumHeaderCharacters)
            {
                throw Refused(
                    $"A column name in the first row of the CSV file is longer than {MaximumHeaderCharacters.ToString("N0", CultureInfo.InvariantCulture)} characters. Shorten it, then import again.");
            }

            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
            afterQuote = false;
        }

        void EndRow()
        {
            EndField();
            rows.Add([.. fields]);
            fields.Clear();
            rowStarted = false;
            if (rows.Count > MaximumRows + 1)
            {
                throw TooManyRows();
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character != '"')
                {
                    field.Append(character);
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                    continue;
                }

                inQuotes = false;
                afterQuote = true;
                continue;
            }

            if (afterQuote && character is not (',' or '\r' or '\n'))
            {
                throw Refused(
                    $"The CSV file has text after a closing quotation mark, at character {index + 1:N0}. Fix the quoting, then import again.");
            }

            switch (character)
            {
                case '"' when !fieldStarted && field.Length == 0:
                    inQuotes = true;
                    fieldStarted = true;
                    rowStarted = true;
                    break;
                case '"':
                    throw Refused(
                        $"The CSV file has a quotation mark inside an unquoted value, at character {index + 1:N0}. Put the value in quotation marks and double the one inside it, then import again.");
                case ',':
                    EndField();
                    rowStarted = true;
                    break;
                case '\r':
                    EndRow();
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(character);
                    fieldStarted = true;
                    rowStarted = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw Refused("The CSV file ends inside a quoted value. Add the missing closing quotation mark, then import again.");
        }

        if (rowStarted || fieldStarted || afterQuote || fields.Count > 0)
        {
            EndRow();
        }

        return rows;
    }

    private static InvalidDataException TooManyRows() => Refused(
        $"The CSV file has more than {MaximumRows.ToString("N0", CultureInfo.InvariantCulture)} rows. "
        + "Split it into files of up to that many rows, then import each one.");

    private static InvalidDataException Refused(string message) =>
        FailureReason.Readable(new InvalidDataException(message));

    public static string Format(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        if (headers.Count == 0)
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        AppendRow(output, headers);
        foreach (var row in rows)
        {
            if (row.Count != headers.Count)
            {
                throw new ArgumentException(
                    "Every exported row must match the header width.",
                    nameof(rows));
            }

            AppendRow(output, row);
        }

        return output.ToString();
    }

    private static void AppendRow(StringBuilder output, IReadOnlyList<string?> fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                output.Append(',');
            }

            var field = fields[index] ?? string.Empty;
            if (field.IndexOfAny([',', '"', '\r', '\n']) >= 0)
            {
                output.Append('"');
                output.Append(field.Replace("\"", "\"\"", StringComparison.Ordinal));
                output.Append('"');
            }
            else
            {
                output.Append(field);
            }
        }

        output.AppendLine();
    }
}

internal sealed record DatabaseGridCsvDocument(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);
