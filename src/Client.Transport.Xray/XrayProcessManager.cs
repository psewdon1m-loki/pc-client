using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Client.Core;

namespace Client.Transport.Xray;

public sealed class XrayProcessManager
{
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(3);
    private Process? _process;
    private string? _xrayExecutablePath;

    public bool IsRunning => _process is { HasExited: false };

    public async Task<OperationResult> StartAsync(XrayRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return OperationResult.Ok("Xray уже запущен.");
        }

        if (!File.Exists(options.XrayExecutablePath))
        {
            return OperationResult.Fail($"Не найден xray.exe: {options.XrayExecutablePath}");
        }

        if (!File.Exists(options.ConfigPath))
        {
            return OperationResult.Fail($"Не найден config.json: {options.ConfigPath}");
        }

        Directory.CreateDirectory(options.WorkingDirectory);
        await KillProcessesByExecutablePathAsync(options.XrayExecutablePath, cancellationToken).ConfigureAwait(false);
        _xrayExecutablePath = Path.GetFullPath(options.XrayExecutablePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.XrayExecutablePath,
            Arguments = $"run -config \"{options.ConfigPath}\"",
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        var error = new StringBuilder();
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                output.AppendLine(args.Data);
            }
        };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                error.AppendLine(args.Data);
            }
        };

        if (!_process.Start())
        {
            return OperationResult.Fail("Не удалось запустить Xray.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        if (_process is null)
        {
            return OperationResult.Fail("Не удалось запустить Xray.");
        }

        var healthy = await WaitForPortAsync(options.HttpPort, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        if (!healthy && _process.HasExited)
        {
            var details = error.Length > 0 ? error.ToString().Trim() : output.ToString().Trim();
            return OperationResult.Fail($"Xray завершился при запуске. ExitCode={_process.ExitCode}. {details}");
        }

        return healthy
            ? OperationResult.Ok("Xray запущен.")
            : OperationResult.Fail("Xray запущен, но локальный HTTP inbound не ответил.");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                await WaitForExitAsync(_process, ProcessExitTimeout, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            if (!string.IsNullOrWhiteSpace(_xrayExecutablePath))
            {
                await KillProcessesByExecutablePathAsync(_xrayExecutablePath, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task<int> KillProcessesByExecutablePathAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(executablePath);
        var processName = Path.GetFileNameWithoutExtension(fullPath);
        var killed = 0;

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (!Path.Equals(process.MainModule?.FileName, fullPath))
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    await WaitForExitAsync(process, ProcessExitTimeout, cancellationToken).ConfigureAwait(false);
                    killed++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Processes can exit or deny module access while we enumerate them.
                }
            }
        }

        return killed;
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var exited = await Task.WhenAny(
                waitTask,
                Task.Delay(timeout, cancellationToken))
            .ConfigureAwait(false);
        if (ReferenceEquals(exited, waitTask))
        {
            await waitTask.ConfigureAwait(false);
        }
        else if (!process.HasExited)
        {
            return;
        }
    }

    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < until && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (SocketException)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }
}
