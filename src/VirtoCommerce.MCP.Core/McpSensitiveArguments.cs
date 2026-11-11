using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VirtoCommerce.MCP.Core.Attributes;

namespace VirtoCommerce.MCP.Core;

public static class McpSensitiveArguments
{
    public const string MaskedValue = "***";

    private const int MaxDepth = 5;
    private const string CollectionSuffix = "[]";
    private const string AnyKey = "*";

    private static readonly string[] _frameworkNamespaces = ["System", "Microsoft", "ModelContextProtocol"];
    private static readonly ConcurrentDictionary<MethodInfo, IReadOnlyList<string>> _pathsByMethod = new();

    public static IReadOnlyList<string> GetPaths(MethodInfo method)
    {
        return _pathsByMethod.GetOrAdd(method, static method => method.GetParameters()
            .SelectMany(parameter => parameter.IsDefined(typeof(McpSensitiveAttribute), inherit: true)
                ? [parameter.Name]
                : GetPaths(parameter.ParameterType, parameter.Name, depth: 1))
            .ToList());
    }

    public static string Mask(string argumentsJson, IEnumerable<string> paths)
    {
        var pathList = paths?.ToList();
        if (string.IsNullOrEmpty(argumentsJson) || pathList is not { Count: > 0 })
        {
            return argumentsJson;
        }

        JsonNode arguments;
        try
        {
            arguments = JsonNode.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return MaskedValue;
        }

        foreach (var path in pathList)
        {
            MaskPath(arguments, path.Split('.'), 0);
        }

        return arguments?.ToJsonString();
    }

    private static IEnumerable<string> GetPaths(Type type, string path, int depth)
    {
        var dictionaryValueType = GetDictionaryValueType(type);
        if (dictionaryValueType != null)
        {
            type = dictionaryValueType;
            path += "." + AnyKey;
        }

        var elementType = dictionaryValueType == null ? GetElementType(type) : null;
        if (elementType != null)
        {
            type = elementType;
            path += CollectionSuffix;
        }

        if (depth > MaxDepth || !IsUserType(type))
        {
            return [];
        }

        // A property is found in the arguments by the name the serializer gives it, which [JsonPropertyName] may change.
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !property.IsDefined(typeof(JsonIgnoreAttribute), inherit: true))
            .SelectMany(property =>
            {
                var propertyPath = $"{path}.{GetJsonName(property)}";
                return property.IsDefined(typeof(McpSensitiveAttribute), inherit: true)
                    ? [propertyPath]
                    : GetPaths(property.PropertyType, propertyPath, depth + 1);
            });
    }

    private static string GetJsonName(PropertyInfo property)
    {
        return property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
    }

    // The value type of a dictionary with string keys, whose values are found under any key
    private static Type GetDictionaryValueType(Type type)
    {
        var dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
            ? type
            : type.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDictionary<,>));

        return dictionary?.GetGenericArguments() is [var keyType, var valueType] && keyType == typeof(string) ? valueType : null;
    }

    private static Type GetElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) && type.GetGenericArguments().Length == 1
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static bool IsUserType(Type type)
    {
        return type.IsClass && type != typeof(string) &&
            !_frameworkNamespaces.Any(ns => type.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true);
    }

    private static void MaskPath(JsonNode node, string[] segments, int index)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                MaskPath(item, segments, index);
            }

            return;
        }

        if (node is not JsonObject value)
        {
            return;
        }

        if (segments[index] == AnyKey)
        {
            foreach (var anyKey in value.Select(x => x.Key).ToList())
            {
                if (index == segments.Length - 1)
                {
                    value[anyKey] = MaskedValue;
                }
                else
                {
                    MaskPath(value[anyKey], segments, index + 1);
                }
            }

            return;
        }

        var name = segments[index].EndsWith(CollectionSuffix, StringComparison.Ordinal) ? segments[index][..^CollectionSuffix.Length] : segments[index];
        var key = value.Select(x => x.Key).FirstOrDefault(x => IsSameName(x, name));
        if (key == null)
        {
            return;
        }

        if (index == segments.Length - 1)
        {
            value[key] = MaskedValue;
            return;
        }

        MaskPath(value[key], segments, index + 1);
    }

    private static bool IsSameName(string jsonName, string clrName)
    {
        return string.Equals(Normalize(jsonName), Normalize(clrName), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string name)
    {
        return name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
