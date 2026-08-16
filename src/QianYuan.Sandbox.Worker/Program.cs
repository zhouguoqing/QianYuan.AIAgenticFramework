using Microsoft.Extensions.Options;
using QianYuan.Core.Sandbox;
using QianYuan.Sandbox.Worker.Configuration;
using QianYuan.Sandbox.Worker.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<SandboxWorkerHostOptions>(builder.Configuration.GetSection("SandboxWorkerHost"));
var hostOptions = builder.Configuration.GetSection("SandboxWorkerHost").Get<SandboxWorkerHostOptions>() ?? new();

builder.Services.AddSingleton(hostOptions);
builder.Services.AddSingleton<ICodeExecutionWorkerClient>(_ => new LocalSandboxCodeExecutionWorkerClient(hostOptions.WorkerId));

var app = builder.Build();

app.MapPost(hostOptions.ExecutePath, async (
    CodeExecutionWorkerRequest request,
    HttpContext httpContext,
    ICodeExecutionWorkerClient worker,
    IOptions<SandboxWorkerHostOptions> options,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("SandboxWorkerEndpoint");
    var currentOptions = options.Value;

    if (!IsRequestAuthorized(httpContext, currentOptions))
    {
        return Results.Unauthorized();
    }

    var result = await worker.ExecuteAsync(request, ct).ConfigureAwait(false);
    httpContext.Response.Headers["X-Sandbox-Worker-Id"] = result.WorkerId;

    logger.LogInformation(
        "Sandbox execution finished. WorkerId={WorkerId} LeaseId={LeaseId} SessionId={SessionId} Attempt={Attempt} Runtime={Runtime} Succeeded={Succeeded} ExitCode={ExitCode} DurationMs={DurationMs}",
        result.WorkerId,
        request.LeaseId,
        request.SessionId,
        request.Attempt,
        request.Runtime,
        result.Succeeded,
        result.ExitCode,
        result.DurationMs);

    return Results.Ok(result);
});

app.MapGet(hostOptions.HealthPath, (IOptions<SandboxWorkerHostOptions> options) =>
{
    var currentOptions = options.Value;
    return Results.Ok(new
    {
        status = "ok",
        workerId = currentOptions.WorkerId,
        now = DateTimeOffset.UtcNow,
    });
});

app.Run();

static bool IsRequestAuthorized(HttpContext httpContext, SandboxWorkerHostOptions options)
{
    if (string.IsNullOrWhiteSpace(options.AuthToken))
    {
        return true;
    }

    if (!httpContext.Request.Headers.TryGetValue("X-Sandbox-Worker-Token", out var incoming))
    {
        return false;
    }

    return string.Equals(incoming.ToString(), options.AuthToken, StringComparison.Ordinal);
}
