using System;
using System.ComponentModel.DataAnnotations;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Domain;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.MCP.Data.Models;

public class McpJobEntity : AuditableEntity, IDataEntity<McpJobEntity, McpJob>
{
    [Required]
    [StringLength(DbContextBase.Length64)]
    public string ScopeName { get; set; }

    [Required]
    [StringLength(DbContextBase.Length128)]
    public string ToolName { get; set; }

    [StringLength(DbContextBase.Length512)]
    public string Title { get; set; }

    [Required]
    [StringLength(DbContextBase.Length32)]
    public string Status { get; set; }

    [StringLength(DbContextBase.Length128)]
    public string EngineJobId { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string UserId { get; set; }

    [StringLength(DbContextBase.Length256)]
    public string UserName { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string RequestedByUserId { get; set; }

    [StringLength(DbContextBase.IdLength)]
    public string PendingChangeId { get; set; }

    public long? ProcessedCount { get; set; }

    public long? TotalCount { get; set; }

    [StringLength(DbContextBase.Length2048)]
    public string Message { get; set; }

    public string Preview { get; set; }

    public DateTime? StartedDate { get; set; }

    public DateTime? FinishedDate { get; set; }

    public virtual McpJob ToModel(McpJob model)
    {
        ArgumentNullException.ThrowIfNull(model);

        model.Id = Id;
        model.CreatedBy = CreatedBy;
        model.CreatedDate = CreatedDate;
        model.ModifiedBy = ModifiedBy;
        model.ModifiedDate = ModifiedDate;

        model.ScopeName = ScopeName;
        model.ToolName = ToolName;
        model.Title = Title;
        model.Status = Status;
        model.EngineJobId = EngineJobId;
        model.UserId = UserId;
        model.UserName = UserName;
        model.RequestedByUserId = RequestedByUserId;
        model.PendingChangeId = PendingChangeId;
        model.ProcessedCount = ProcessedCount;
        model.TotalCount = TotalCount;
        model.Message = Message;
        model.Preview = Preview;
        model.StartedDate = StartedDate;
        model.FinishedDate = FinishedDate;

        return model;
    }

    public virtual McpJobEntity FromModel(McpJob model, PrimaryKeyResolvingMap pkMap)
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
        Title = model.Title;
        Status = model.Status;
        EngineJobId = model.EngineJobId;
        UserId = model.UserId;
        UserName = model.UserName;
        RequestedByUserId = model.RequestedByUserId;
        PendingChangeId = model.PendingChangeId;
        ProcessedCount = model.ProcessedCount;
        TotalCount = model.TotalCount;
        Message = model.Message;
        Preview = model.Preview;
        StartedDate = model.StartedDate;
        FinishedDate = model.FinishedDate;

        return this;
    }

    public virtual void Patch(McpJobEntity target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Title = Title;
        target.Status = Status;
        target.EngineJobId = EngineJobId;
        target.ProcessedCount = ProcessedCount;
        target.TotalCount = TotalCount;
        target.Message = Message;
        target.StartedDate = StartedDate;
        target.FinishedDate = FinishedDate;
    }
}
