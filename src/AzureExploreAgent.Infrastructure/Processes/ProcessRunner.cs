using System.Diagnostics;

namespace AzureExploreAgent.Infrastructure.Processes;

/// <summary>
/// Starts a child process directly (never through a shell) so arguments are passed verbatim via
/// <see cref="ProcessStartInfo.ArgumentList"/> and shell syntax such as <c>;</c>, <c>&amp;&amp;</c> or <c>|</c> is never interpreted.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start '{request.FileName}'.");
        }

        // No interactive prompt can ever be answered, so close stdin immediately.
        process.StandardInput.Close();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true);
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask,
            TimedOut: false);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
    }
}
