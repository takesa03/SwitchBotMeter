using System;
using System.Globalization;
using System.Windows.Data;

namespace SwitchBotMeter.Converters;

// ConverterParameter は "trueのときの文字;falseのときの文字" の形式で指定する
public class BoolToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? ";").Split(';');
        bool flag = value is bool b && b;
        return flag ? parts[0] : (parts.Length > 1 ? parts[1] : "");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
