using System.Windows;

namespace WhisperWind.App.Controls;

/// <summary>模板里用的附加属性：图标字形、序号（壹贰叁…）</summary>
public static class Ex
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ex), new FrameworkPropertyMetadata(""));

    public static string GetIcon(DependencyObject o) => (string)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string v) => o.SetValue(IconProperty, v);

    public static readonly DependencyProperty NumeralProperty = DependencyProperty.RegisterAttached(
        "Numeral", typeof(string), typeof(Ex), new FrameworkPropertyMetadata(""));

    public static string GetNumeral(DependencyObject o) => (string)o.GetValue(NumeralProperty);
    public static void SetNumeral(DependencyObject o, string v) => o.SetValue(NumeralProperty, v);
}
