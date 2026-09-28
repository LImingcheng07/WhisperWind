using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WhisperWind.App.Services;

namespace WhisperWind.App.Converters;

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush True { get; set; } = Brushes.LimeGreen;
    public Brush False { get; set; } = Brushes.Gray;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? True : False;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool x && x;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NotBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Convert(value, targetType, parameter, culture);
}

/// <summary>value.ToString() == parameter → true；ConvertBack 用于 RadioButton 选中时回写 parameter</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}

/// <summary>value.ToString() == parameter → Visible（参数可用 | 分隔多个值）</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value?.ToString();
        bool eq = false;
        foreach (var p in (parameter?.ToString() ?? "").Split('|'))
            if (p == v) { eq = true; break; }
        if (Invert) eq = !eq;
        return eq ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null / 空字符串 / 0 → Collapsed</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool empty = value switch
        {
            null => true,
            string s => s.Length == 0,
            int i => i == 0,
            _ => false,
        };
        if (Invert) empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>自检等级 → 颜色</summary>
public sealed class CheckLevelToBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze(Color.FromRgb(0x7F, 0x9A, 0x74));
    private static readonly Brush Info = Freeze(Color.FromRgb(0x8A, 0x9B, 0xB0));
    private static readonly Brush Warn = Freeze(Color.FromRgb(0xD9, 0x8A, 0x3A));
    private static readonly Brush Error = Freeze(Color.FromRgb(0xC0, 0x46, 0x3A));

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CheckLevel.Ok => Ok,
        CheckLevel.Info => Info,
        CheckLevel.Warn => Warn,
        _ => Error,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → 两段文字之一</summary>
public sealed class BoolTextConverter : IValueConverter
{
    public string True { get; set; } = "";
    public string False { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? True : False;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
