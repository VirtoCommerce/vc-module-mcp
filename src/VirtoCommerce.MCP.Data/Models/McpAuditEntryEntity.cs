using System;
using System.ComponentModel.DataAnnotations;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Domain;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.MCP.Data.Models;

public class McpAuditEntryEntity : AuditableEntity, IDataEntity<McpAuditEntryEntity, McpAuditEntry>
{
    [Required]
    [StringLength(DbContextBase.Length64)]
    public string ScopeName { get; set; }

    [Required]
    [StringLength(DbContextBase.Length128)]
    public string ToolName { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string UserId { get; set; }

    [StringLength(DbContextBase.Length256)]
    public string UserName { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string ClientId { get; set; }

    public string Arguments { get; set; }

    [Required]
    [StringLength(DbContextBase.Length32)]
    public string Outcome { get; set; }

    [StringLength(DbContextBase.Length2048)]
    public string ErrorMessage { get; set; }

    public long DurationMs { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string PendingChangeId { get; set; }

    public virtual McpAuditEntry ToModel(McpAuditEntry model)
    {
        ArgumentNullException.ThrowIfNull(model);

        model.Id = Id;
        model.CreatedBy = CreatedBy;
        model.CreatedDate = CreatedDate;
        model.ModifiedBy = ModifiedBy;
        model.ModifiedDate = ModifiedDate;

        model.ScopeName = ScopeName;
        model.ToolName = ToolName;
        model.UserId = UserId;
        model.UserName = UserName;
        model.ClientId = ClientId;
        model.Arguments = Arguments;
        model.Outcome = Outcome;
        model.ErrorMessage = ErrorMessage;
        model.DurationMs = DurationMs;
        model.PendingChangeId = PendingChangeId;

        return model;
    }

    public virtual McpAuditEntryEntity FromModel(McpAuditEntry model, PrimaryKeyResolvingMap pkMap)
    {
        ArgumentNullException.ThrowIfNull(model);

        pkMap.AddPair(model, this);

        Id = model.Id;
        CreatedBy = model.CreatedBy;
        CreatedDate = model.CreatedDate;
        ModifiedBy = model.ModifiedBy;
        ModifiedDate = model.ModifiedDate;

        ScopeName = model.ScopeName;
        ToolName = model.ToolName;
        UserId = model.UserId;
        UserName = model.UserName;
        ClientId = model.ClientId;
        Arguments = model.Arguments;
        Outcome = model.Outcome;
        ErrorMessage = model.ErrorMessage;
        DurationMs = model.DurationMs;
        PendingChangeId = model.PendingChangeId;

        return this;
    }

    public virtual void Patch(McpAuditEntryEntity target)
    {
        ArgumentNullException.ThrowIfNull(target);
    }
}
