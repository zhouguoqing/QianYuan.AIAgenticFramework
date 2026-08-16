using System.Diagnostics;
using QianYuan.Core.Sandbox;

namespace QianYuan.Sandbox.Worker.Services;

public sealed class LocalSandboxCodeExecutionWorkerClient : ICodeExecutionWorkerClient
{
    private readonly string _workerId;

    public LocalSandboxCodeExecutionWorkerClient(string workerId)
    {
        _workerId = string.IsNullOrWhiteSpace(workerId) ? "sandbox-worker" : workerId.Trim();
    }

    public async ValueTask<CodeExecutionWorkerResponse> ExecuteAsync(CodeExecutionWorkerRequest request, CancellationToken ct = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var (fileName, suffix) = request.Runtime switch
            {
                "python" => ("python3", ".py"),
                "node" => ("node", ".js"),
                "bash" => ("bash", ".sh"),
                _ => (string.Empty, string.Empty)
            };

            if (string.IsNullOrEmpty(fileName))
            {
                return Failed("UNSUPPORTED_RUNTIME", $"unsupported runtime '{request.Runtime}'", request, startedAt);
            }

            Directory.CreateDirectory(request.WorkingDirectory);
            var snippetPath = Path.Combine(request.WorkingDirectory, $"snippet_{Guid.NewGuid():N}{suffix}");
            await File.WriteAllTextAsync(snippetPath, request.Code, ct).ConfigureAwait(false);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(request.Timeout);

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = request.WorkingDirectory,
                };
                psi.ArgumentList.Add(snippetPath);

                using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {fileName}");
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                return new CodeExecutionWorkerResponse
                {
                    Succeeded = true,
                    TimedOut = false,
                    ExitCode = proc.ExitCode,
                    Stdout = Truncate(stdout, request.MaxOutputChars),
                    Stderr = Truncate(stderr, request.MaxOutputChars),
                    WorkerId = _workerId,
                    Attempt = request.Attempt,
                    DurationMs = ElapsedMs(startedAt),
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new CodeExecutionWorkerResponse
                {
                    Succeeded = false,
                    TimedOut = true,
                    ExitCode = -1,
                    WorkerId = _workerId,
                    Attempt = request.Attempt,
                    DurationMs = ElapsedMs(startedAt),
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = $"execution timed out after {request.Timeout}",
                };
            }
            finally
            {
                try { File.Delete(snippetPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            return Failed("WORKER_EXCEPTION", ex.Message, request, startedAt);
        }
    }

    private CodeExecutionWorkerResponse Failed(string code, string message, CodeExecutionWorkerRequest request, long startedAt)
        => new()
        {
            Succeeded = false,
            TimedOut = false,
            ExitCode = -1,
            WorkerId = _workerId,
            Attempt = request.Attempt,
            DurationMs = ElapsedMs(startedAt),
            ErrorCode = code,
            ErrorMessage = message,
        };

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "...[truncated]" : s;

    private static long ElapsedMs(long startedAt)
        => (long)(1000.0 * (Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency);
}
