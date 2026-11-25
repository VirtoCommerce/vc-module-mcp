using System;

namespace VirtoCommerce.MCP.Core.Services;

// How a setup compares values: a value from a request (a JsonElement), a stored one or a default. A solution changes the
// rule for every setup by registering its own.
public interface IMcpSetupValues
{
    // Numbers and booleans by value when either side is one (10 equals "10.0", true equals "True"), two texts by their
    // invariant text exactly, as the platform compares setting values; null equals an empty text.
    bool AreEqual(object left, object right);

    // As AreEqual, with texts compared as given: StringComparison.OrdinalIgnoreCase finds an allowed value whatever the
    // case it is sent in.
    bool AreEqual(object left, object right, StringComparison textComparison);

    // The value as the invariant text AreEqual compares: a JSON string's text, a JSON number's or boolean's raw text, a
    // date in the round-trip format.
    string ToInvariantString(object value);
}
