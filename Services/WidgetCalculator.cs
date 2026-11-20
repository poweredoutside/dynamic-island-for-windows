using System.Globalization;

namespace DynamicIslandWin.Services;

internal sealed class WidgetCalculator
{
    private string _entry = "0";
    private decimal _left;
    private string? _operation;
    private bool _startNew;
    internal bool HasError { get; private set; }
    internal string Display => _entry.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

    internal void Press(string key)
    {
        if (key == "AC" || HasError)
        {
            _entry = "0";
            _operation = null;
            _startNew = false;
            HasError = false;
            if (key == "AC") return;
        }

        if (key is "+" or "−" or "×" or "÷" or "=")
        {
            try
            {
                decimal current = Number();
                if (!_startNew && _operation != null)
                {
                    current = _operation switch
                    {
                        "+" => _left + current, "−" => _left - current, "×" => _left * current, _ => _left / current
                    };
                }
                _entry = Format(current);
                _left = current;
                _operation = key == "=" ? null : key;
                _startNew = true;
            }
            catch (ArithmeticException)
            {
                HasError = true;
                _entry = "0";
            }
            return;
        }

        switch (key)
        {
            case "±": _entry = Number() == 0 ? "0" : _entry.StartsWith('-') ? _entry[1..] : "-" + _entry; break;
            case "%": _entry = Format(Number() / 100); break;
            case "⌫":
                if (!_startNew) _entry = _entry.Length <= 1 || _entry.Length == 2 && _entry[0] == '-' ? "0" : _entry[..^1];
                break;
            default:
                if (_startNew) { _entry = "0"; _startNew = false; }
                if (key == ".")
                {
                    if (!_entry.Contains('.')) _entry += ".";
                }
                else if (_entry.Length < 16) _entry = _entry == "0" ? key : _entry + key;
                break;
        }
        if (key is "±" or "%") _startNew = false;
    }

    private decimal Number() => decimal.Parse(_entry, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
