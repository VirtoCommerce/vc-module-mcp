using System;
using System.Globalization;
using System.Text.Json;

namespace VirtoCommerce.MCP.Core.Services;

// The MCP module's rule for comparing setup values (IMcpSetupValues).
public class McpSetupValues : IMcpSetupValues
{
    public virtual bool AreEqual(object left, object right)
    {
        return AreEqual(left, right, StringComparison.Ordinal);
    }

    // A number or a boolean on either side compares by value: 10 equals "10.0", true equals "True". Two texts compare as
    // texts, so "007" and "7" differ, as the platform compares setting values.
    public virtual bool AreEqual(object left, object right, StringComparison textComparison)
    {
        var leftText = ToInvariantString(left);
        var rightText = ToInvariantString(right);

        if ((IsNumber(left) || IsNumber(right))
            && decimal.TryParse(leftText, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(rightText, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        if ((IsBoolean(left) || IsBoolean(right))
            && bool.TryParse(leftText, out var leftBoolean) && bool.TryParse(rightText, out var rightBoolean))
        {
            return leftBoolean == rightBoolean;
        }

        return string.Equals(leftText ?? string.Empty, rightText ?? string.Empty, textComparison);
    }

    public virtual string ToInvariantString(object value)
    {
        return value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement element => element.GetRawText(),
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    // A number as a value, not as a text: a CLR number, or a JSON number from a request.
    protected virtual bool IsNumber(object value)
    {
        return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            or JsonElement { ValueKind: JsonValueKind.Number };
    }

    // A boolean as a value, not as a text: a CLR boolean, or a JSON true or false from a request.
    protected virtual bool IsBoolean(object value)
    {
        return value is bool or JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False };
    }
}
