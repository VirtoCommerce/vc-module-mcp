namespace VirtoCommerce.MCP.Core.Settings;

// The settings of Virto Commerce modules known to be secrets. McpSecretSettings treats them as secrets whatever their name
// and type, so no module registers them. The list was found by running the rule over the settings of the installed
// modules; a secret the rule cannot find is added here by hand.
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
