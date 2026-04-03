namespace DaisiGit.Worker;

/// <summary>
/// Message sent via Azure Storage Queue to dispatch a workflow execution to the worker.
/// </summary>
public class WorkflowDispatchMessage
{
    public string ExecutionId { get; set; } = "";
    public string AccountId { get; set; } = "";
}
