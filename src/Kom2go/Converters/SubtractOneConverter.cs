using System.Globalization;

namespace Kom2go.Converters;

/// <summary>
/// Subtracts one from the value (useful for Maximum binding on sliders)
/// </summary>
public class SubtractOneConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return Math.Max(0, intValue - 1);
        }
        if (value is double doubleValue)
        {
            return Math.Max(0, doubleValue - 1);
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue + 1;
        }
        if (value is double doubleValue)
        {
            return doubleValue + 1;
        }
        return value;
    }
}

/// <summary>
/// Returns true if the value is not null
/// </summary>
public class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns true if the value is null
/// </summary>
public class IsNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return value ?? false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return value ?? false;
    }
}

/// <summary>
/// Returns true if the value equals the parameter
/// </summary>
public class IsEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null && parameter is null)
        {
            return true;
        }
        if (value is null || parameter is null)
        {
            return false;
        }
        
        // Handle numeric comparison
        if (value is int intValue && int.TryParse(parameter.ToString(), out var intParam))
        {
            return intValue == intParam;
        }
        
        return value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns true if the value does not equal the parameter
/// </summary>
public class IsNotEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null && parameter is null)
        {
            return false;
        }
        if (value is null || parameter is null)
        {
            return true;
        }
        
        // Handle numeric comparison
        if (value is int intValue && int.TryParse(parameter.ToString(), out var intParam))
        {
            return intValue != intParam;
        }
        
        return !value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
