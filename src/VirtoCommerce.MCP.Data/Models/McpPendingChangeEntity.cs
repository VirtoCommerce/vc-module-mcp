using System;
using System.ComponentModel.DataAnnotations;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Domain;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.MCP.Data.Models;

public class McpPendingChangeEntity : AuditableEntity, IDataEntity<McpPendingChangeEntity, McpPendingChange>
{
    [Required]
    [StringLength(DbContextBase.Length64)]
    public string ScopeName { get; set; }

    [Required]
    [StringLength(DbContextBase.Length128)]
    public string ToolName { get; set; }

    public string Arguments { get; set; }

    public string Preview { get; set; }

    [Required]
    [StringLength(DbContextBase.Length32)]
    public string Status { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string RequestedByUserId { get; set; }

    [StringLength(DbContextBase.Length256)]
    public string RequestedByUserName { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string ReviewedByUserId { get; set; }

    [StringLength(DbContextBase.Length256)]
    public string ReviewedByUserName { get; set; }

    public DateTime? ReviewedDate { get; set; }

    [StringLength(DbContextBase.Length2048)]
    public string ReviewComment { get; set; }

    public DateTime ExpirationDate { get; set; }

    public string Result { get; set; }

    public virtual McpPendingChange ToModel(McpPendingChange model)
    {
        ArgumentNullException.ThrowIfNull(model);

        model.Id = Id;
        model.CreatedBy = CreatedBy;
        model.CreatedDate = CreatedDate;
        model.ModifiedBy = ModifiedBy;
        model.ModifiedDate = ModifiedDate;

        model.ScopeName = ScopeName;
        model.ToolName = ToolName;
        model.Arguments = Arguments;
        model.Preview = Preview;
        model.Status = Status;
        model.RequestedByUserId = RequestedByUserId;
        model.RequestedByUserName = RequestedByUserName;
        model.ReviewedByUserId = ReviewedByUserId;
        model.ReviewedByUserName = ReviewedByUserName;
        model.ReviewedDate = ReviewedDate;
        model.ReviewComment = ReviewComment;
        model.ExpirationDate = ExpirationDate;
        model.Result = Result;

        return model;
    }

    public virtual McpPendingChangeEntity FromModel(McpPendingChange model, PrimaryKeyResolvingMap pkMap)
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
        Arguments = model.Arguments;
        Preview = model.Preview;
        Status = model.Status;
        RequestedByUserId = model.RequestedByUserId;
        RequestedByUserName = model.RequestedByUserName;
        ReviewedByUserId = model.ReviewedByUserId;
        ReviewedByUserName = model.ReviewedByUserName;
        ReviewedDate = model.ReviewedDate;
        ReviewComment = model.ReviewComment;
        ExpirationDate = model.ExpirationDate;
        Result = model.Result;

        return this;
    }

    public virtual void Patch(McpPendingChangeEntity target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Status = Status;
        target.ReviewedByUserId = ReviewedByUserId;
        target.ReviewedByUserName = ReviewedByUserName;
        target.ReviewedDate = ReviewedDate;
        target.ReviewComment = ReviewComment;
        target.Result = Result;
    }
}
