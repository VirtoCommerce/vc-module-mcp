using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtoCommerce.MCP.Core.Attributes;

namespace VirtoCommerce.MCP.Core;

public static class McpSensitiveArguments
{
    public const string MaskedValue = "***";

    private const int MaxDepth = 5;
    private const string CollectionSuffix = "[]";

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
        var elementType = GetElementType(type);
        if (elementType != null)
        {
            type = elementType;
            path += CollectionSuffix;
        }

        if (depth > MaxDepth || !IsUserType(type))
        {
            return [];
        }

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(property => property.IsDefined(typeof(McpSensitiveAttribute), inherit: true)
                ? [$"{path}.{property.Name}"]
                : GetPaths(property.PropertyType, $"{path}.{property.Name}", depth + 1));
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
