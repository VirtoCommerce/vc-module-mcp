using VirtoCommerce.MCP.MarketingSample.Data;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Models;

// Extends the promotion of the marketing sample with the finance cost center that Contoso books discounts to.
// Registered with AbstractTypeFactory<DemoPromotion>.OverrideType, so every promotion the sample creates, the seeded
// ones included, is a ContosoPromotion, and the sample's tools keep the field when they copy or save a promotion.
public class ContosoPromotion : DemoPromotion
{
    public string CostCenter { get; set; }
}
