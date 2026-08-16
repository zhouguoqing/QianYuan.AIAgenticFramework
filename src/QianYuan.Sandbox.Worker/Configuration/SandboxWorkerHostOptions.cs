namespace QianYuan.Sandbox.Worker.Configuration;

public sealed class SandboxWorkerHostOptions
{
    public string WorkerId { get; set; } = "sandbox-worker-1";
    public string? AuthToken { get; set; }
    public string ExecutePath { get; set; } = "/api/internal/sandbox/code-exec";
    public string HealthPath { get; set; } = "/health";
}
