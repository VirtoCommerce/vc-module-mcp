using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;

namespace VirtoCommerce.MCP.Testing;

// Checks the tools of an assembly against the conventions of an MCP toolset, the way the agent will see them, in one
// test:
//   [Fact] public void Toolset_FollowsTheConventions() => McpToolsetConventions.Verify<McpAssemblyMarker>();
// A deliberate exception is written down where it is made:
//   McpToolsetConventions.Verify<McpAssemblyMarker>(x => x.Ignore(McpConventionRules.Paging, "list_regions", "a fixed list of 12"));
public static partial class McpToolsetConventions
{
    private static readonly string[] _readVerbs = ["get", "search", "list", "find", "explain", "analyze", "describe", "check", "track"];
    private static readonly string[] _listVerbs = ["search", "list"];
    private static readonly string[] _dryRunNames = ["dryRun", "dry_run"];

    public static void Verify<TAssemblyMarker>(Action<McpConventionOptions> configure = null)
    {
        Verify(typeof(TAssemblyMarker).Assembly, configure);
    }

    // Throws McpConventionException listing every violation.
    public static void Verify(Assembly assembly, Action<McpConventionOptions> configure = null)
    {
        ThrowIfAny(Check(assembly, configure));
    }

    public static void Verify(IEnumerable<Type> toolTypes, Action<McpConventionOptions> configure = null)
    {
        ThrowIfAny(Check(toolTypes, configure));
    }

    private static void ThrowIfAny(IReadOnlyList<McpConventionViolation> violations)
    {
        if (violations.Count > 0)
        {
            throw new McpConventionException(violations);
        }
    }

    public static IReadOnlyList<McpConventionViolation> Check<TAssemblyMarker>(Action<McpConventionOptions> configure = null)
    {
        return Check(typeof(TAssemblyMarker).Assembly, configure);
    }

    public static IReadOnlyList<McpConventionViolation> Check(Assembly assembly, Action<McpConventionOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Check(assembly.GetTypes(), configure);
    }

    // The tool classes given, such as one class while a toolset is being written.
    public static IReadOnlyList<McpConventionViolation> Check(IEnumerable<Type> toolTypes, Action<McpConventionOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(toolTypes);

        var options = new McpConventionOptions();
        configure?.Invoke(options);

        var tools = FindTools(toolTypes).ToList();
        var violations = new List<McpConventionViolation>();

        foreach (var (method, tool) in tools)
        {
            violations.AddRange(CheckTool(method, tool));
        }

        violations.AddRange(CheckUniqueNames(tools.Select(x => x.Tool)));

        return violations
            .Where(x => !options.IsIgnored(x.Rule, x.ToolName))
            .OrderBy(x => x.ToolName, StringComparer.Ordinal)
            .ThenBy(x => x.Rule, StringComparer.Ordinal)
            .ToList();
    }

    // The tool methods of the assembly, created the way the SDK creates them, so the names, annotations and metadata
    // are the ones the agent and the MCP module see.
    private static IEnumerable<(MethodInfo Method, McpServerTool Tool)> FindTools(IEnumerable<Type> types)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in types.Where(x => x.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(flags).Where(x => x.GetCustomAttribute<McpServerToolAttribute>() != null))
            {
                var tool = method.IsStatic
                    ? McpServerTool.Create(method, target: null, new McpServerToolCreateOptions { SerializerOptions = McpSerialization.Options })
                    : McpServerTool.Create(method, _ => null, new McpServerToolCreateOptions { SerializerOptions = McpSerialization.Options });

                yield return (method, tool);
            }
        }
    }

    private static IEnumerable<McpConventionViolation> CheckTool(MethodInfo method, McpServerTool tool)
    {
        var name = tool.ProtocolTool.Name;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
        var annotations = tool.ProtocolTool.Annotations;
        var readOnly = annotations?.ReadOnlyHint == true;
        var verb = name.Split('_')[0];
        var parameters = method.GetParameters();

        if (string.IsNullOrEmpty(attribute?.Name))
        {
            yield return Violation(McpConventionRules.Name, name, "Set Name on [McpServerTool]; the name is the tool's contract with agents.");
        }
        else if (!ToolNameRegex().IsMatch(attribute.Name))
        {
            yield return Violation(McpConventionRules.Name, name, "The name is not snake_case verb_noun, such as search_promotions or pause_promotion.");
        }

        if (string.IsNullOrWhiteSpace(tool.ProtocolTool.Description))
        {
            yield return Violation(McpConventionRules.Description, name, "Add [Description]: when to use the tool, what it returns, and when to use another one.");
        }

        foreach (var parameter in parameters.Where(x => IsArgument(x) && string.IsNullOrWhiteSpace(x.GetCustomAttribute<DescriptionAttribute>()?.Description)))
        {
            yield return Violation(McpConventionRules.ArgumentDescription, name, $"Describe the argument '{parameter.Name}': its meaning, its units and what omitting it means.");
        }

        if (tool.GetMcpScopes().Count == 0)
        {
            yield return Violation(McpConventionRules.Scope, name, "Add [McpScope]: a tool without a scope is not served anywhere.");
        }

        if (!tool.Metadata.Any(x => x is IAuthorizeData or IAllowAnonymous or McpPermissionAttribute or McpAnyUserAttribute))
        {
            yield return Violation(McpConventionRules.Access, name, "Say who may use the tool: [Authorize], [McpPermission], [McpAnyUser] or [AllowAnonymous]. Without it a scope that requires a signed-in user does not serve it.");
        }

        if (_readVerbs.Contains(verb) && !readOnly)
        {
            yield return Violation(McpConventionRules.ReadOnly, name, $"A '{verb}_' tool reads: set ReadOnly = true, so clients can call it without asking.");
        }

        if (readOnly && (tool.RequiresApproval() || tool.GetConfirmation() != null))
        {
            yield return Violation(McpConventionRules.ReadOnly, name, "The tool is ReadOnly but asks for approval or confirmation; one of them is wrong.");
        }

        var hasDryRun = parameters.Any(x => _dryRunNames.Contains(x.Name) && (x.ParameterType == typeof(bool) || x.ParameterType == typeof(bool?)));
        if (!readOnly && !hasDryRun)
        {
            yield return Violation(McpConventionRules.DryRun, name, tool.RequiresApproval()
                ? "The tool requires approval but has no bool dryRun: the MCP module refuses its calls, because the preview is what the approver reviews."
                : "A tool that changes data takes a bool dryRun and returns the same preview (McpChangePreview) without the change.");
        }

        if (!readOnly && annotations?.DestructiveHint != false && !tool.RequiresApproval() && tool.GetConfirmation() == null)
        {
            yield return Violation(McpConventionRules.Safeguard, name, "The tool may destroy data without a safeguard: add [McpRequiresApproval] or [McpRequiresConfirmation], or set Destructive = false when it only adds.");
        }

        if (_listVerbs.Contains(verb) && !(HasIntArgument(parameters, "take") && HasIntArgument(parameters, "skip")))
        {
            yield return Violation(McpConventionRules.Paging, name, "A list tool takes int? take and int? skip and returns the total (McpPage<T>), so no answer is cut off silently.");
        }

        foreach (var violation in CheckApprovalCondition(tool, name))
        {
            yield return violation;
        }

        foreach (var violation in CheckConfirmation(tool, name, parameters))
        {
            yield return violation;
        }
    }

    private static IEnumerable<McpConventionViolation> CheckApprovalCondition(McpServerTool tool, string name)
    {
        var condition = tool.Metadata.OfType<McpRequiresApprovalAttribute>().FirstOrDefault()?.Condition;
        if (string.IsNullOrEmpty(condition))
        {
            yield break;
        }

        if (tool.FindApprovalCondition() == null)
        {
            yield return Violation(McpConventionRules.ApprovalCondition, name,
                $"The approval condition '{condition}' is not a public static bool method of the tool class taking IReadOnlyDictionary<string, JsonElement>; every call would need approval.");
        }
    }

    private static IEnumerable<McpConventionViolation> CheckConfirmation(McpServerTool tool, string name, ParameterInfo[] parameters)
    {
        var message = tool.GetConfirmation()?.Message;
        if (string.IsNullOrEmpty(message))
        {
            yield break;
        }

        foreach (var placeholder in PlaceholderRegex().Matches(message).Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal))
        {
            if (parameters.All(x => !string.Equals(x.Name, placeholder, StringComparison.Ordinal)))
            {
                yield return Violation(McpConventionRules.ConfirmationMessage, name, $"The confirmation message names '{{{placeholder}}}', which is not an argument of the tool.");
            }
        }
    }

    private static IEnumerable<McpConventionViolation> CheckUniqueNames(IEnumerable<McpServerTool> tools)
    {
        return tools
            .SelectMany(tool => tool.GetMcpScopes().Select(scope => (Scope: scope, Tool: tool)))
            .GroupBy(x => (x.Scope, x.Tool.ProtocolTool.Name))
            .Where(x => x.Count() > 1)
            .Select(x => Violation(McpConventionRules.UniqueName, x.Key.Name, $"{x.Count()} tools are named '{x.Key.Name}' in scope '{x.Key.Scope}'; the MCP module serves only one."));
    }

    private static bool HasIntArgument(ParameterInfo[] parameters, string name)
    {
        return parameters.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal) && (x.ParameterType == typeof(int) || x.ParameterType == typeof(int?)));
    }

    // An argument the agent fills in, as opposed to a service the tool gets from DI: a value of a simple type, or a list
    // of them. Complex input types are not checked, their properties are described where they are declared. dryRun means
    // the same in every tool and the MCP module describes it, so it needs no description of its own.
    private static bool IsArgument(ParameterInfo parameter)
    {
        return !_dryRunNames.Contains(parameter.Name) && IsSimple(parameter.ParameterType);
    }

    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(JsonElement))
        {
            return true;
        }

        if (type.IsArray)
        {
            return IsSimple(type.GetElementType()!);
        }

        return type.IsGenericType &&
               type.GetGenericArguments().Length == 1 &&
               typeof(System.Collections.IEnumerable).IsAssignableFrom(type) &&
               IsSimple(type.GetGenericArguments()[0]);
    }

    private static McpConventionViolation Violation(string rule, string toolName, string message)
    {
        return new McpConventionViolation(rule, toolName, message);
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)+$")]
    private static partial Regex ToolNameRegex();

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderRegex();
}

public static class McpConventionRules
{
    // An explicit snake_case verb_noun name.
    public const string Name = "name";

    // One tool of a name in a scope.
    public const string UniqueName = "unique-name";

    // A description the model can choose the tool by.
    public const string Description = "description";

    // Every argument of a simple type is described.
    public const string ArgumentDescription = "argument-description";

    // [McpScope]
    public const string Scope = "scope";

    // [Authorize], [McpPermission], [McpAnyUser] or [AllowAnonymous]
    public const string Access = "access";

    // Reading verbs are ReadOnly; a ReadOnly tool does not ask for approval or confirmation.
    public const string ReadOnly = "read-only";

    // A tool that changes data takes dryRun.
    public const string DryRun = "dry-run";

    // A destructive tool asks for approval or confirmation.
    public const string Safeguard = "safeguard";

    // search_ and list_ tools take take and skip.
    public const string Paging = "paging";

    // The Condition of [McpRequiresApproval] exists.
    public const string ApprovalCondition = "approval-condition";

    // The {argument} placeholders of [McpRequiresConfirmation] are arguments.
    public const string ConfirmationMessage = "confirmation-message";
}

public sealed record McpConventionViolation(string Rule, string ToolName, string Message)
{
    public override string ToString()
    {
        return $"{ToolName} [{Rule}]: {Message}";
    }
}

public sealed class McpConventionOptions
{
    private readonly List<(string Rule, string ToolName, string Reason)> _ignored = [];

    public IReadOnlyList<(string Rule, string ToolName, string Reason)> Ignored => _ignored;

    // Ignores a rule for one tool, with the reason the exception is deliberate; a tool name of "*" ignores the rule for
    // every tool.
    public McpConventionOptions Ignore(string rule, string toolName, string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(rule);
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));

        _ignored.Add((rule, toolName, reason));
        return this;
    }

    public bool IsIgnored(string rule, string toolName)
    {
        return _ignored.Any(x => string.Equals(x.Rule, rule, StringComparison.Ordinal) &&
                                 (x.ToolName == "*" || string.Equals(x.ToolName, toolName, StringComparison.Ordinal)));
    }
}

public sealed class McpConventionException(IReadOnlyList<McpConventionViolation> violations)
    : Exception($"The toolset breaks {violations.Count} MCP convention(s):{Environment.NewLine}{string.Join(Environment.NewLine, violations.Select(x => "- " + x))}")
{
    public IReadOnlyList<McpConventionViolation> Violations { get; } = violations;
}
