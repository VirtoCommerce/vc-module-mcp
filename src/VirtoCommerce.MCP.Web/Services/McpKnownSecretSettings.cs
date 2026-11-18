namespace VirtoCommerce.MCP.Web.Services;

// The settings of Virto Commerce modules known to be secrets. The list was found by running the secret rule of
// McpSettingsService over the settings of the installed modules; a secret the rule cannot find is added here by hand.
// The Store module keeps the same list for its store setup tools: the two modules share no code.
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
