using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Jobs;
using VirtoCommerce.MCP.MarketingSample.Services;

namespace VirtoCommerce.MCP.MarketingSample.Tools;

// The marketing content toolset: what the storefront shows in each placeholder, the gaps, why a customer sees a
// banner, and publishing. Publishing that goes live today needs approval; scheduling for later does not.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
public class ContentTools(
    ContentDemoStore content,
    MarketingDemoStore marketing,
    IMcpJobDispatcher jobDispatcher)
{
    private const string Everyone = "everyone";

    // Hero banners show about this many characters of a headline.
    private const int HeroHeadlineLength = 80;

    private const string FlatArgumentsJustification = "Each parameter is an argument of the MCP tool's input schema; an agent fills flat arguments more reliably than a nested object.";

    [McpServerTool(Name = "get_placement_map", ReadOnly = true, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentReadPermission)]
    [Description("Every placeholder of a store on a date: what is published into it, for whom, what comes next, and the empty slots.")]
    public object GetPlacementMap(
        [Description("Store id.")] string store_id,
        [Description("Date, yyyy-MM-dd. Defaults to today.")] DateTime? date = null)
    {
        var store = GetStore(store_id);
        var day = date ?? content.Now;
        var publications = content.GetPublications(store.Id);

        return new
        {
            store_id = store.Id,
            date = MarketingFormat.Date(day),
            placeholders = content.GetPlaceholders(store.Id).Select(placeholder =>
            {
                var live = publications.Where(x => x.Placeholder == placeholder.Name && x.IsLive(day)).OrderByDescending(x => x.Priority).ToList();
                var next = publications.Where(x => x.Placeholder == placeholder.Name && x.StartDate > day).OrderBy(x => x.StartDate).FirstOrDefault();

                return new
                {
                    placeholder = placeholder.Name,
                    page = placeholder.Page,
                    shows = live.Select(x => new { publication_id = x.Id, item = content.GetItem(x.ItemId)?.Name, audience = x.SegmentId ?? Everyone, until = MarketingFormat.Date(x.EndDate) }),
                    next = next == null ? null : new { publication_id = next.Id, item = content.GetItem(next.ItemId)?.Name, from = MarketingFormat.Date(next.StartDate) },
                    empty_for_most_customers = !live.Any(x => x.SegmentId == null),
                };
            }),
        };
    }

    [McpServerTool(Name = "list_content_placeholders", ReadOnly = true, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentReadPermission)]
    [Description("The placeholders of a store: name, page and what they are for.")]
    public object ListContentPlaceholders([Description("Store id.")] string store_id)
    {
        var store = GetStore(store_id);

        return new
        {
            store_id = store.Id,
            placeholders = content.GetPlaceholders(store.Id).Select(x => new { placeholder = x.Name, page = x.Page, description = x.Description }),
        };
    }

    [McpServerTool(Name = "search_content_items", ReadOnly = true, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentReadPermission)]
    [Description("Find content items (banners, HTML blocks, product carousels) by words, folder or type, with where they are published.")]
    public async Task<object> SearchContentItems(
        IMcpPageSizeResolver pageSizeResolver,
        [Description("Words in the name or the headline.")] string keyword = null,
        [Description("Folder, for example 'Banners/Black Friday'.")] string folder = null,
        [Description("banner, html_block or product_carousel.")] string content_type = null,
        [Description("Number of rows to return. Omit it for the default page size.")] int? take = null)
    {
        var words = keyword?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var items = content.GetItems()
            .Where(x => string.IsNullOrEmpty(folder) || x.Folder.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrEmpty(content_type) || string.Equals(x.ContentType, content_type, StringComparison.OrdinalIgnoreCase))
            .Where(x => words.All(w => x.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || x.Headline?.Contains(w, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(x => x.Folder).ThenBy(x => x.Name)
            .ToList();
        var publications = content.GetPublications();
        var pageSize = await pageSizeResolver.GetPageSizeAsync(take);

        return new
        {
            total_count = items.Count,
            items = items.Take(pageSize).Select(x => new
            {
                item_id = x.Id,
                name = x.Name,
                type = x.ContentType,
                folder = x.Folder,
                languages = x.Languages,
                promotion_id = x.PromotionId,
                modified = MarketingFormat.Date(x.ModifiedDate),
                published = publications.Where(p => p.ItemId == x.Id).Select(p => $"{p.StoreId}/{p.Placeholder} {MarketingFormat.Date(p.StartDate)} to {MarketingFormat.Date(p.EndDate)}"),
            }),
        };
    }

    [McpServerTool(Name = "find_content_gaps", ReadOnly = true, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentReadPermission)]
    [Description("Content problems in the next days: placeholders with nothing published for most customers, publications that " +
        "end without a successor, publications whose audience can never match (a deleted segment), and banners of a promotion " +
        "that is not live while the banner is.")]
    public object FindContentGaps(
        [Description("Store id. Omit it for every store.")] string store_id = null,
        [Description("Number of days ahead to check. Defaults to 30.")] int days = 30)
    {
        var today = content.Now.Date;
        var to = today.AddDays(Math.Clamp(days, 1, 120));
        var stores = string.IsNullOrEmpty(store_id) ? marketing.GetStores() : [GetStore(store_id)];
        var findings = new List<object>();

        foreach (var storeId in stores.Select(store => store.Id))
        {
            var publications = content.GetPublications(storeId);

            foreach (var placeholder in content.GetPlaceholders(storeId))
            {
                findings.AddRange(FindPlaceholderGaps(storeId, placeholder.Name, publications, today, to));
            }

            findings.AddRange(FindUnreachableAudiences(storeId, publications, today));
            findings.AddRange(FindPromotionMismatches(storeId, publications, today, to));
        }

        return new { from = MarketingFormat.Date(today), to = MarketingFormat.Date(to), total_count = findings.Count, findings };
    }

    [McpServerTool(Name = "explain_content_resolution", ReadOnly = true, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentReadPermission)]
    [Description("Which publication the storefront renders in a placeholder for a customer of a segment on a date, and why the others are not shown. " +
        "Use it to prove targeting before approving a publication.")]
    public object ExplainContentResolution(
        [Description("Store id.")] string store_id,
        [Description("Placeholder name.")] string placeholder,
        [Description("Segment of the customer, for example vip. Omit it for a customer without a segment.")] string segment_id = null,
        [Description("Date, yyyy-MM-dd. Defaults to today.")] DateTime? date = null)
    {
        var store = GetStore(store_id);
        var slot = content.GetPlaceholder(store.Id, placeholder) ?? throw new McpException($"Store {store.Id} has no placeholder '{placeholder}'. Use list_content_placeholders.");
        var day = date ?? content.Now;

        var candidates = content.GetPublications(store.Id)
            .Where(x => x.Placeholder == slot.Name)
            .OrderByDescending(x => x.Priority)
            .Select(x => (Publication: x, Reason: GetHiddenReason(x, store.Id, segment_id, day)))
            .ToList();
        var winner = candidates.FirstOrDefault(x => x.Reason == null).Publication;

        return new
        {
            store_id = store.Id,
            placeholder = slot.Name,
            customer_segment = segment_id ?? "none",
            date = MarketingFormat.Date(day),
            shown = DescribeShown(winner),
            not_shown = candidates.Where(x => x.Publication != winner).Select(x => new
            {
                publication_id = x.Publication.Id,
                item = content.GetItem(x.Publication.ItemId)?.Name,
                reason = x.Reason ?? $"a publication with priority {winner?.Priority} wins over priority {x.Publication.Priority}",
            }),
        };
    }

    [McpServerTool(Name = "create_content_item", ReadOnly = false, Destructive = false, Idempotent = false)]
    [McpPermission(MarketingSampleConstants.ContentCreatePermission)]
    [Description("Create a content item (banner, HTML block or product carousel). It is not shown anywhere until it is published with publish_content.")]
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = FlatArgumentsJustification)]
    public object CreateContentItem(
        [Description("Name of the item.")] string name,
        [Description("Folder, for example 'Banners/Black Friday'.")] string folder,
        [Description("banner, html_block or product_carousel.")] string content_type,
        [Description("Headline shown to customers.")] string headline,
        [Description("Body text.")] string body = null,
        [Description("Promotion the item advertises.")] string promotion_id = null,
        [Description("Languages the item has text for, for example ['en-US', 'de-DE'].")] string[] languages = null,
        bool dryRun = false)
    {
        if (content_type is not ("banner" or "html_block" or "product_carousel"))
        {
            throw new McpException("content_type must be banner, html_block or product_carousel.");
        }

        if (promotion_id != null && marketing.GetPromotion(promotion_id) == null)
        {
            throw new McpException($"Promotion '{promotion_id}' was not found.");
        }

        var item = new DemoContentItem
        {
            Id = $"item-{Guid.NewGuid():N}"[..13],
            Name = name,
            Folder = folder,
            ContentType = content_type,
            Headline = headline,
            Body = body,
            PromotionId = promotion_id,
            Languages = languages ?? ["en-US"],
        };

        var preview = new McpChangePreview { Action = "create_content_item", ObjectType = "content_item", ObjectId = item.Id, ObjectName = item.Name, DryRun = dryRun }
            .AddChange("folder", null, folder)
            .AddChange("headline", null, headline);

        if (!dryRun)
        {
            content.SaveItem(item);
        }

        return preview;
    }

    [McpServerTool(Name = "propose_content_copy", ReadOnly = false, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentUpdatePermission)]
    [McpRequiresApproval]
    [Description("Propose new customer-facing text for a content item in one language, for example copy you wrote from a brief. " +
        "The proposal is stored as a pending change with the old and the new text side by side; it is applied only when an " +
        "approver approves it with approve_pending_change. Tell the user the change id.")]
    public object ProposeContentCopy(
        [Description("Content item id.")] string item_id,
        [Description("Language of the text, for example de-DE.")] string language,
        [Description("New headline.")] string headline,
        [Description("New body text.")] string body = null,
        [Description("The brief the text answers, kept with the proposal for the approver.")] string brief = null,
        bool dryRun = false)
    {
        var item = content.GetItem(item_id) ?? throw new McpException($"Content item '{item_id}' was not found. Use search_content_items.");
        if (string.IsNullOrWhiteSpace(headline))
        {
            throw new McpException("headline must not be empty.");
        }

        var preview = new McpChangePreview { Action = "apply_copy", ObjectType = "content_item", ObjectId = item.Id, ObjectName = item.Name, DryRun = dryRun }
            .AddChange($"headline ({language})", item.Headline, headline)
            .AddChange($"body ({language})", item.Body, body ?? item.Body);
        if (!string.IsNullOrEmpty(brief))
        {
            preview.AddWarning($"Brief: {brief}");
        }

        if (headline.Length > HeroHeadlineLength)
        {
            preview.AddWarning($"The headline has {headline.Length} characters; hero banners show about {HeroHeadlineLength}.");
        }

        if (!dryRun)
        {
            item.Headline = headline;
            item.Body = body ?? item.Body;
            item.Languages = item.Languages.Union([language], StringComparer.OrdinalIgnoreCase).ToArray();
            content.SaveItem(item);
        }

        return preview;
    }

    [McpServerTool(Name = "publish_content", ReadOnly = false, Destructive = true, Idempotent = false)]
    [McpPermission(MarketingSampleConstants.ContentUpdatePermission)]
    [McpRequiresApproval(Condition = nameof(GoesLiveToday))]
    [Description("Show a content item in a placeholder of a store for a period, optionally for one customer segment. Scheduling " +
        "for a later day applies at once; a window that includes today goes live immediately and is submitted for approval.")]
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = FlatArgumentsJustification)]
    public object PublishContent(
        [Description("Content item id, from search_content_items.")] string item_id,
        [Description("Store id.")] string store_id,
        [Description("Placeholder name, from list_content_placeholders.")] string placeholder,
        [Description("First day, yyyy-MM-dd.")] DateTime start_date,
        [Description("Last day, yyyy-MM-dd.")] DateTime end_date,
        [Description("Only for customers of this segment. Omit it for everyone.")] string segment_id = null,
        [Description("Higher wins when publications overlap. Defaults to 10.")] int priority = 10,
        bool dryRun = false)
    {
        var store = GetStore(store_id);
        var item = content.GetItem(item_id) ?? throw new McpException($"Content item '{item_id}' was not found. Use search_content_items.");
        var slot = content.GetPlaceholder(store.Id, placeholder) ?? throw new McpException($"Store {store.Id} has no placeholder '{placeholder}'. Use list_content_placeholders.");

        var publication = new DemoPublication
        {
            Id = dryRun ? "(new)" : content.NewPublicationId(),
            StoreId = store.Id,
            Placeholder = slot.Name,
            ItemId = item.Id,
            StartDate = MarketingFormat.StartOfDay(start_date).Value,
            EndDate = MarketingFormat.EndOfDay(end_date).Value,
            SegmentId = segment_id,
            Priority = priority,
        };
        ValidateWindow(publication);

        var preview = Describe("publish", publication, item);
        preview.DryRun = dryRun;
        if (!dryRun)
        {
            content.SavePublication(publication);
        }

        return preview;
    }

    [McpServerTool(Name = "reschedule_publication", ReadOnly = false, Destructive = true, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentUpdatePermission)]
    [McpRequiresApproval(Condition = nameof(GoesLiveToday))]
    [Description("Change the dates of a publication. A window that starts today or earlier is submitted for approval.")]
    public object ReschedulePublication(
        [Description("Publication id.")] string publication_id,
        [Description("New first day, yyyy-MM-dd.")] DateTime start_date,
        [Description("New last day, yyyy-MM-dd.")] DateTime end_date,
        bool dryRun = false)
    {
        var publication = content.GetPublication(publication_id) ?? throw new McpException($"Publication '{publication_id}' was not found.");
        var target = publication.Clone();
        target.StartDate = MarketingFormat.StartOfDay(start_date).Value;
        target.EndDate = MarketingFormat.EndOfDay(end_date).Value;
        ValidateWindow(target);

        var preview = Describe("reschedule", target, content.GetItem(target.ItemId))
            .AddChange("start", MarketingFormat.Date(publication.StartDate), MarketingFormat.Date(target.StartDate))
            .AddChange("end", MarketingFormat.Date(publication.EndDate), MarketingFormat.Date(target.EndDate));
        preview.DryRun = dryRun;

        if (!dryRun)
        {
            content.SavePublication(target);
        }

        return preview;
    }

    [McpServerTool(Name = "unpublish_content", ReadOnly = false, Destructive = true, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ContentUpdatePermission)]
    [Description("Take a publication down at once, for example a banner of a paused promotion. It needs no approval.")]
    public object UnpublishContent(
        [Description("Publication id.")] string publication_id,
        bool dryRun = false)
    {
        var publication = content.GetPublication(publication_id) ?? throw new McpException($"Publication '{publication_id}' was not found.");
        var preview = new McpChangePreview { Action = "unpublish", ObjectType = "publication", ObjectId = publication.Id, ObjectName = content.GetItem(publication.ItemId)?.Name, DryRun = dryRun }
            .AddChange("placeholder", $"{publication.StoreId}/{publication.Placeholder}", null);

        if (!dryRun)
        {
            content.DeletePublication(publication.Id);
        }

        return preview;
    }

    [McpServerTool(Name = "clone_content_campaign", ReadOnly = false, Destructive = false, Idempotent = false)]
    [McpPermission(MarketingSampleConstants.ContentUpdatePermission)]
    [Description("Copy publications to another store, shifted in time, matching placeholders by name: 'the same Black Friday banners " +
        "for the Fashion store, a week later'. Runs as a background job; call it with dryRun first to see what maps where.")]
    public async Task<object> CloneContentCampaign(
        RequestContext<CallToolRequestParams> context,
        [Description("Publication ids to copy.")] string[] publication_ids,
        [Description("Store to copy to.")] string target_store_id,
        [Description("Days to move the dates by. Defaults to 0.")] int shift_days = 0,
        bool dryRun = false)
    {
        var target = GetStore(target_store_id);
        var plan = (publication_ids ?? [])
            .Select(id => content.GetPublication(id) ?? throw new McpException($"Publication '{id}' was not found."))
            .Select(x => new
            {
                publication_id = x.Id,
                item = content.GetItem(x.ItemId)?.Name,
                placeholder = x.Placeholder,
                copied = content.GetPlaceholder(target.Id, x.Placeholder) != null,
                note = content.GetPlaceholder(target.Id, x.Placeholder) == null ? $"store {target.Id} has no placeholder '{x.Placeholder}'; skipped" : $"{x.StartDate.AddDays(shift_days):yyyy-MM-dd} to {x.EndDate.AddDays(shift_days):yyyy-MM-dd}",
            })
            .ToList();

        if (plan.Count == 0)
        {
            throw new McpException("Pass at least one publication id.");
        }

        if (dryRun)
        {
            return new { dry_run = true, target_store_id = target.Id, shift_days, publications = plan };
        }

        return await jobDispatcher.EnqueueAsync<CloneContentCampaignJob>(context,
            new CloneContentCampaignPayload { PublicationIds = publication_ids, TargetStoreId = target.Id, ShiftDays = shift_days },
            new McpJobRequest { Title = $"Copy {plan.Count(x => x.copied)} publications to {target.Id}", TotalCount = plan.Count, Preview = plan });
    }

    // Approval condition of publish_content and reschedule_publication: only a window that is live today needs a second person.
    public static bool GoesLiveToday(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("start_date", out var value) || value.ValueKind != JsonValueKind.String ||
            !DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start))
        {
            return true;
        }

        return start.Date <= DateTime.UtcNow.Date;
    }

    private McpChangePreview Describe(string action, DemoPublication publication, DemoContentItem item)
    {
        var preview = new McpChangePreview { Action = action, ObjectType = "publication", ObjectId = publication.Id, ObjectName = item?.Name }
            .AddChange("placeholder", null, $"{publication.StoreId}/{publication.Placeholder}")
            .AddChange("window", null, $"{publication.StartDate:yyyy-MM-dd} to {publication.EndDate:yyyy-MM-dd}")
            .AddChange("audience", null, publication.SegmentId ?? Everyone);

        AddAudienceWarning(preview, publication);
        AddLanguageWarning(preview, publication, item);
        AddOverlapWarnings(preview, publication);
        AddPromotionDatesWarning(preview, publication, item);

        return preview;
    }

    private void AddAudienceWarning(McpChangePreview preview, DemoPublication publication)
    {
        if (publication.SegmentId != null && !SegmentExists(publication.StoreId, publication.SegmentId))
        {
            preview.AddWarning($"Segment '{publication.SegmentId}' does not exist in store {publication.StoreId}: no customer would see it.");
        }
    }

    private void AddLanguageWarning(McpChangePreview preview, DemoPublication publication, DemoContentItem item)
    {
        var storeLanguages = marketing.GetStore(publication.StoreId)?.Languages ?? [];
        var missing = storeLanguages.Except(item?.Languages ?? [], StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
        {
            preview.AddWarning($"The item has no text for {string.Join(", ", missing)}.");
        }
    }

    // Other publications of the same placeholder, window and audience: the priority decides which one is shown.
    private void AddOverlapWarnings(McpChangePreview preview, DemoPublication publication)
    {
        foreach (var other in content.GetPublications(publication.StoreId).Where(x => Competes(x, publication)))
        {
            var name = content.GetItem(other.ItemId)?.Name;
            preview.AddWarning(other.Priority.CompareTo(publication.Priority) switch
            {
                > 0 => $"'{name}' ({other.Id}) has priority {other.Priority} in the same window and wins over this one.",
                < 0 => $"This one replaces '{name}' ({other.Id}) while both run.",
                _ => $"'{name}' ({other.Id}) has the same priority {other.Priority} in the same window: which one is shown is not defined. Change the priority.",
            });
        }
    }

    private void AddPromotionDatesWarning(McpChangePreview preview, DemoPublication publication, DemoContentItem item)
    {
        var promotion = marketing.GetPromotion(item?.PromotionId);
        if (promotion != null && (promotion.StartDate > publication.StartDate || promotion.EndDate < publication.EndDate))
        {
            preview.AddWarning($"The banner advertises '{promotion.Name}', which runs {MarketingFormat.Date(promotion.StartDate)} to " +
                $"{MarketingFormat.Date(promotion.EndDate) ?? "no end"}; align the dates.");
        }
    }

    // Empty slots of a placeholder for most customers, and publications for everyone that end without a successor.
    private IEnumerable<object> FindPlaceholderGaps(string storeId, string placeholder, IReadOnlyList<DemoPublication> publications, DateTime today, DateTime to)
    {
        var forEveryone = publications.Where(x => x.Placeholder == placeholder && x.SegmentId == null).ToList();
        foreach (var (from, until) in FindEmptyRanges(forEveryone, today, to))
        {
            yield return new
            {
                kind = "empty_slot",
                store_id = storeId,
                placeholder,
                from = MarketingFormat.Date(from),
                to = MarketingFormat.Date(until),
                message = $"Nothing is published for most customers from {from:yyyy-MM-dd} to {until:yyyy-MM-dd}.",
            };
        }

        foreach (var publication in forEveryone.Where(x => x.EndDate >= today && x.EndDate < to && !HasSuccessor(x, forEveryone)))
        {
            yield return new
            {
                kind = "no_successor",
                store_id = storeId,
                placeholder,
                publication_id = publication.Id,
                item = content.GetItem(publication.ItemId)?.Name,
                message = $"Ends {publication.EndDate:yyyy-MM-dd} and nothing follows it.",
            };
        }
    }

    // Publications for a segment that no longer exists in the store.
    private IEnumerable<object> FindUnreachableAudiences(string storeId, IReadOnlyList<DemoPublication> publications, DateTime today)
    {
        return publications
            .Where(x => x.SegmentId != null && x.EndDate >= today && !SegmentExists(storeId, x.SegmentId))
            .Select(publication => new
            {
                kind = "unreachable_audience",
                store_id = storeId,
                placeholder = publication.Placeholder,
                publication_id = publication.Id,
                item = content.GetItem(publication.ItemId)?.Name,
                message = $"Targets segment '{publication.SegmentId}', which does not exist in the store: no customer ever sees it.",
            });
    }

    // Banners of a promotion that is not live, or ends earlier, while the banner runs.
    private IEnumerable<object> FindPromotionMismatches(string storeId, IReadOnlyList<DemoPublication> publications, DateTime today, DateTime to)
    {
        foreach (var publication in publications.Where(x => x.EndDate >= today && x.StartDate <= to))
        {
            var promotion = marketing.GetPromotion(content.GetItem(publication.ItemId)?.PromotionId);
            if (promotion != null && IsNotLiveDuring(promotion, publication))
            {
                yield return new
                {
                    kind = "promotion_mismatch",
                    store_id = storeId,
                    placeholder = publication.Placeholder,
                    publication_id = publication.Id,
                    item = content.GetItem(publication.ItemId)?.Name,
                    message = $"Advertises promotion '{promotion.Id}', which is {promotion.GetStatus(content.Now)} and ends " +
                        $"{MarketingFormat.Date(promotion.EndDate) ?? "never"}, while the banner runs until {publication.EndDate:yyyy-MM-dd}.",
                };
            }
        }
    }

    // Why the storefront does not show a publication to a customer of the segment on the day; null when it may show it.
    private string GetHiddenReason(DemoPublication publication, string storeId, string segmentId, DateTime day)
    {
        if (publication.StartDate > day)
        {
            return $"starts {publication.StartDate:yyyy-MM-dd}";
        }

        if (publication.EndDate < day)
        {
            return $"ended {publication.EndDate:yyyy-MM-dd}";
        }

        if (publication.SegmentId == null)
        {
            return null;
        }

        if (!SegmentExists(storeId, publication.SegmentId))
        {
            return $"targets segment '{publication.SegmentId}', which does not exist";
        }

        return string.Equals(publication.SegmentId, segmentId, StringComparison.OrdinalIgnoreCase) ? null : $"only for segment '{publication.SegmentId}'";
    }

    private object DescribeShown(DemoPublication winner)
    {
        if (winner == null)
        {
            return null;
        }

        return new { publication_id = winner.Id, item = content.GetItem(winner.ItemId)?.Name, headline = content.GetItem(winner.ItemId)?.Headline, priority = winner.Priority };
    }

    private void ValidateWindow(DemoPublication publication)
    {
        if (publication.EndDate < publication.StartDate)
        {
            throw new McpException("The last day must not be before the first day.");
        }

        if (publication.EndDate < content.Now)
        {
            throw new McpException("The window is in the past.");
        }
    }

    private DemoStore GetStore(string storeId)
    {
        return marketing.GetStore(storeId) ?? throw new McpException($"Store '{storeId}' was not found. Stores: {string.Join(", ", marketing.GetStores().Select(x => x.Id))}.");
    }

    private bool SegmentExists(string storeId, string segmentId)
    {
        return marketing.GetSegment(segmentId)?.StoreIds.Contains(storeId) == true;
    }

    private static bool HasSuccessor(DemoPublication publication, List<DemoPublication> publications)
    {
        return publications.Any(x => x.Id != publication.Id && x.StartDate <= publication.EndDate.AddDays(1) && x.EndDate > publication.EndDate);
    }

    private static bool IsNotLiveDuring(DemoPromotion promotion, DemoPublication publication)
    {
        return promotion.GetStatus(publication.StartDate) is PromotionStatus.Draft or PromotionStatus.Paused || promotion.EndDate < publication.EndDate;
    }

    // Same placeholder, overlapping windows, and an audience that includes the publication's audience.
    private static bool Competes(DemoPublication other, DemoPublication publication)
    {
        return other.Id != publication.Id && other.Placeholder == publication.Placeholder && WindowsOverlap(other, publication) && Reaches(other, publication.SegmentId);
    }

    // A publication for everyone reaches every segment; one for a segment reaches only that segment.
    private static bool Reaches(DemoPublication publication, string segmentId)
    {
        return publication.SegmentId == null || publication.SegmentId == segmentId;
    }

    private static bool WindowsOverlap(DemoPublication a, DemoPublication b)
    {
        return a.StartDate <= b.EndDate && b.StartDate <= a.EndDate;
    }

    private static IEnumerable<(DateTime From, DateTime To)> FindEmptyRanges(IReadOnlyCollection<DemoPublication> publications, DateTime from, DateTime to)
    {
        DateTime? start = null;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var covered = publications.Any(x => x.StartDate.Date <= day && day <= x.EndDate.Date);
            if (!covered && start == null)
            {
                start = day;
            }
            else if (covered && start != null)
            {
                yield return (start.Value, day.AddDays(-1));
                start = null;
            }
        }

        if (start != null)
        {
            yield return (start.Value, to);
        }
    }
}
