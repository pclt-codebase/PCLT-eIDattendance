using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pclt.EidAttendance.Eid.Validation;

public static class BelgianNationalNumberValidator
{
    public static bool IsValid(string nationalNumber)
    {
        var normalized = Normalize(nationalNumber);
        if (normalized.Length != 11)
        {
            return false;
        }

        var firstNineDigits = normalized[..9];
        if (!long.TryParse(firstNineDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var baseNumber))
        {
            return false;
        }

        if (!int.TryParse(normalized[9..11], NumberStyles.None, CultureInfo.InvariantCulture, out var providedChecksum))
        {
            return false;
        }

        var expectedOldRule = CalculateChecksum(baseNumber);
        if (providedChecksum == expectedOldRule)
        {
            return true;
        }

        var expectedNewRule = CalculateChecksum(2000000000L + baseNumber);
        return providedChecksum == expectedNewRule;
    }

    public static string BuildValid(DateTime birthDate, int sequence)
    {
        if (sequence is < 1 or > 997)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Volgnummer moet tussen 1 en 997 liggen.");
        }

        var firstNineDigits = $"{birthDate:yyMMdd}{sequence:000}";
        var baseNumber = long.Parse(firstNineDigits, CultureInfo.InvariantCulture);

        var checksum = birthDate.Year >= 2000
            ? CalculateChecksum(2000000000L + baseNumber)
            : CalculateChecksum(baseNumber);

        return $"{firstNineDigits}{checksum:00}";
    }

    public static string Normalize(string value)
    {
        return Regex.Replace(value ?? string.Empty, "[^0-9]", string.Empty);
    }

    private static int CalculateChecksum(long value)
    {
        var checksum = 97 - (int)(value % 97);
        return checksum == 0 ? 97 : checksum;
    }
}
