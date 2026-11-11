using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using Xunit;

namespace VirtoCommerce.MCP.Tests;

[Trait("Category", "Unit")]
public class McpSensitiveArgumentsTests
{
    [Fact]
    public void GetPaths_Should_FindSensitiveParameters_And_NestedProperties()
    {
        // Arrange
        var method = typeof(ContactTools).GetMethod(nameof(ContactTools.UpdateContact));

        // Act
        var paths = McpSensitiveArguments.GetPaths(method);

        // Assert
        Assert.Equal(["email", "contact.Phone", "contact.Address.Line1", "addresses[].Line1"], paths);
    }

    [Fact]
    public void GetPaths_Should_FindNothing_When_NoArgumentIsSensitive()
    {
        // Act
        var paths = McpSensitiveArguments.GetPaths(typeof(ContactTools).GetMethod(nameof(ContactTools.FindContact)));

        // Assert
        Assert.Empty(paths);
    }

    [Fact]
    public void Mask_Should_HideTheValuesAtThePaths_Whatever_TheJsonNamingPolicy()
    {
        // Arrange
        const string json = """{"contactId":"c1","email":"a@b.c","contact":{"name":"Ann","phone":"123","address":{"line_1":"Main St","city":"Oslo"}},"addresses":[{"line1":"A"},{"LINE1":"B"}]}""";

        // Act
        var masked = JsonNode.Parse(McpSensitiveArguments.Mask(json, ["email", "contact.Phone", "contact.Address.Line1", "addresses[].Line1"]));

        // Assert
        Assert.Equal("c1", masked["contactId"]!.GetValue<string>());
        Assert.Equal(McpSensitiveArguments.MaskedValue, masked["email"]!.GetValue<string>());
        Assert.Equal("Ann", masked["contact"]!["name"]!.GetValue<string>());
        Assert.Equal(McpSensitiveArguments.MaskedValue, masked["contact"]!["phone"]!.GetValue<string>());
        Assert.Equal(McpSensitiveArguments.MaskedValue, masked["contact"]!["address"]!["line_1"]!.GetValue<string>());
        Assert.Equal("Oslo", masked["contact"]!["address"]!["city"]!.GetValue<string>());
        Assert.Equal(McpSensitiveArguments.MaskedValue, masked["addresses"]![0]!["line1"]!.GetValue<string>());
        Assert.Equal(McpSensitiveArguments.MaskedValue, masked["addresses"]![1]!["LINE1"]!.GetValue<string>());
    }

    [Fact]
    public void GetPaths_Should_UseTheJsonNames_And_ReachTheValuesOfDictionaries()
    {
        // Act
        var paths = McpSensitiveArguments.GetPaths(typeof(ContactTools).GetMethod(nameof(ContactTools.ImportContacts))!);

        // Assert
        Assert.Equal(["contacts.*.Phone", "contacts.*.secret_note"], paths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Mask_Should_HideTheValuesUnderEveryKeyOfADictionary()
    {
        // Arrange
        const string json = """{"contacts":{"c1":{"secret_note":"x","Name":"Ann"},"c2":{"secret_note":"y"}}}""";

        // Act
        var masked = JsonNode.Parse(McpSensitiveArguments.Mask(json, ["contacts.*.secret_note"]));

        // Assert
        Assert.Equal(McpSensitiveArguments.MaskedValue, masked["contacts"]!["c1"]!["secret_note"]!.GetValue<string>());
        Assert.Equal(McpSensitiveArguments.MaskedValue, masked["contacts"]!["c2"]!["secret_note"]!.GetValue<string>());
        Assert.Equal("Ann", masked["contacts"]!["c1"]!["Name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"email":"a@b.c"}""", null, """{"email":"a@b.c"}""")]
    [InlineData("""{"email":"a@b.c"}""", "", """{"email":"a@b.c"}""")]
    [InlineData("""{"name":"Ann"}""", "email", """{"name":"Ann"}""")]
    [InlineData("not json", "email", McpSensitiveArguments.MaskedValue)]
    public void Mask_Should_HandleMissingPathsAndValues(string json, string path, string expected)
    {
        // Act
        var masked = McpSensitiveArguments.Mask(json, string.IsNullOrEmpty(path) ? [] : [path]);

        // Assert
        Assert.Equal(expected, masked);
    }

    public static class ContactTools
    {
        public static string UpdateContact(string contactId, [McpSensitive] string email, ContactInput contact, List<AddressInput> addresses) => contactId;

        public static string FindContact(string keyword, int take) => keyword;

        public static string ImportContacts(Dictionary<string, ImportedContact> contacts) => "imported";
    }

    public sealed class ContactInput
    {
        public string Name { get; set; }

        [McpSensitive]
        public string Phone { get; set; }

        public AddressInput Address { get; set; }
    }

    public sealed class ImportedContact
    {
        public string Name { get; set; }

        [McpSensitive]
        [JsonPropertyName("secret_note")]
        public string Note { get; set; }

        [McpSensitive]
        public string Phone { get; set; }
    }

    public sealed class AddressInput
    {
        [McpSensitive]
        public string Line1 { get; set; }

        public string City { get; set; }
    }
}
