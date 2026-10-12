using System.Globalization;

namespace Asura.Application;

/// <summary>
/// Sizes as a person reads them. One implementation, so a file's size in a
/// listing, in a preview's details and inside an archive all read the same.
/// Units are decimal (1 KB = 1000 bytes), as macOS Finder and
/// <c>ByteCountFormatter</c> count them and as Docker and Git report sizes, so
/// two panels never show the same file under two different numbers.
/// </summary>
public static class ByteSize
{
    private const double Step = 1000;

    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        if (bytes < Step)
        {
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} {units[0]}";
        }

        var value = (double)bytes;
        var unit = 0;
        while (value >= Step && unit < units.Length - 1)
        {
            value /= Step;
            unit++;
        }

        // 999,960 bytes rounds to "1000 KB"; carry it into the next unit so
        // the number stays below 1000 the way ByteCountFormatter shows it.
        if (Math.Round(value, 1) >= Step && unit < units.Length - 1)
        {
            value /= Step;
            unit++;
        }

        return $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
