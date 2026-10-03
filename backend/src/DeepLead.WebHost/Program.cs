using System.Diagnostics;

// Windows-service wrapper for the Next.js web app: runs `node server.js` from the standalone build and restarts it if it exits.
// Settings (appsettings.json / environment): Web:Directory, Web:NodePath, Web:Port, Web:Hostname.

if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "DeepLead Web");
builder.Services.AddHostedService<NodeServer>();
builder.Build().Run();

sealed class NodeServer(IConfiguration config, ILogger<NodeServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = config["Web:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "..", "web");
        var node = config["Web:NodePath"] ?? "node";
        var port = config["Web:Port"] ?? "3000";
        var hostname = config["Web:Hostname"] ?? "0.0.0.0";

        while (!stoppingToken.IsCancellationRequested)
        {
            var start = new ProcessStartInfo(node, "server.js")
            {
                WorkingDirectory = Path.GetFullPath(directory),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.Environment["PORT"] = port;
            start.Environment["HOSTNAME"] = hostname;
            start.Environment["NODE_ENV"] = "production";

            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("[web] {Line}", e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("[web] {Line}", e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                logger.LogInformation("Web app started on port {Port} (pid {Pid})", port, process.Id);
                await process.WaitForExitAsync(stoppingToken);
                logger.LogWarning("Web app exited with code {Code}; restarting in 5 s", process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not start the web app ({Node} server.js in {Dir})", node, directory);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { });
        }
    }
}
