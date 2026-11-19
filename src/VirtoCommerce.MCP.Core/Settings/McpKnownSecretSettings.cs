namespace VirtoCommerce.MCP.Core.Settings;

// The settings of Virto Commerce modules known to be secrets. The list was found by running McpSecretSettings over the
// settings of the installed modules; a secret the rule cannot find is added here by hand. A module whose tools describe
// settings registers it with AddMcpSecretSettings, as the MCP module does for the settings tools.
public static class McpKnownSecretSettings
{
    public static readonly string[] SettingNames =
    [
        "ApplicationInsights.InstrumentationKey",
        "BuilderIO.PublicApiKey",
        "Contentful.DeliveryApiKey",
        "Contentful.PreviewApiKey",
        "Contract.ContractPassword",
        "ElasticAppSearch.ElasticAppSearchPassword",
        "GDPR.GDPRPassword",
        "PlatformBackup.ZipPassword",
        "PushMessages.FcmReceiverOptions.ApiKey",
        "PushMessages.FcmReceiverOptions.VapidKey",
        "Return.ReturnPassword",
        "Sanity.ApiToken",
        "Shipping.Bopis.GoogleMaps.ApiKey",
        "ShipStation.ShipStationPassword",
    ];
}
