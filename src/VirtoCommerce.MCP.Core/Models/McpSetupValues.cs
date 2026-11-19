using System;
using System.Globalization;
using System.Text.Json;

namespace VirtoCommerce.MCP.Core.Models;

// Setting and field values as a setup compares them: a value from a request (a JsonElement), a stored one or a default.
public static class McpSetupValues
{
    // Numbers and booleans by value (10 equals 10.0, true equals "True"), anything else by its invariant text exactly, as
    // the platform compares setting values; null equals an empty text.
    public static bool AreEqual(object left, object right)
    {
        return AreEqual(left, right, StringComparison.Ordinal);
    }

    // As AreEqual, with texts compared as given: StringComparison.OrdinalIgnoreCase finds an allowed value whatever the
    // case it is sent in.
    public static bool AreEqual(object left, object right, StringComparison textComparison)
    {
        var leftText = ToInvariantString(left);
        var rightText = ToInvariantString(right);

        if (decimal.TryParse(leftText, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(rightText, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        if (bool.TryParse(leftText, out var leftBoolean) && bool.TryParse(rightText, out var rightBoolean))
        {
            return leftBoolean == rightBoolean;
        }

        return string.Equals(leftText ?? string.Empty, rightText ?? string.Empty, textComparison);
    }

    public static string ToInvariantString(object value)
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
}
