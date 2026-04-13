namespace System;

internal static class StringCompatibilityExtensions
{
    public static bool Contains(this string value, string comparisonValue, StringComparison comparisonType)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (comparisonValue is null)
        {
            throw new ArgumentNullException(nameof(comparisonValue));
        }

        return value.IndexOf(comparisonValue, comparisonType) >= 0;
    }

    public static string Replace(this string value, string oldValue, string newValue, StringComparison comparisonType)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (string.IsNullOrEmpty(oldValue))
        {
            throw new ArgumentException("Value cannot be null or empty.", nameof(oldValue));
        }

        newValue = newValue ?? string.Empty;

        var index = value.IndexOf(oldValue, comparisonType);
        if (index < 0)
        {
            return value;
        }

        var result = value;
        while (index >= 0)
        {
            result = result.Substring(0, index) + newValue + result.Substring(index + oldValue.Length);
            index = result.IndexOf(oldValue, index + newValue.Length, comparisonType);
        }

        return result;
    }

    public static bool StartsWith(this string value, char character)
    {
        return !string.IsNullOrEmpty(value) && value[0] == character;
    }

    public static string[] Split(this string value, char separator, int count, StringSplitOptions options)
    {
        return value.Split(new[] { separator }, count, options);
    }

    public static string[] Split(this string value, char separator, StringSplitOptions options)
    {
        return value.Split(new[] { separator }, options);
    }
}
