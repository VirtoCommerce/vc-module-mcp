namespace VirtoCommerce.MCP.Web.Jobs;

// Wraps the payload of a job started by an MCP tool, so the MCP job record follows the job's progress and outcome.
public class McpTrackedJobPayload
{
    public string McpJobId { get; set; }

    // Assembly-qualified name of the module's job handler.
    public string HandlerType { get; set; }

    // Assembly-qualified name of the payload type the handler is registered for.
    public string HandlerPayloadType { get; set; }

    // Assembly-qualified name of the payload object, which may derive from HandlerPayloadType.
    public string PayloadType { get; set; }

    public string Payload { get; set; }
}
