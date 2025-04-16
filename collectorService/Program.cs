using System.Runtime.InteropServices;
using collectorService;

var builder = Host.CreateApplicationBuilder(args);

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    builder.Services.AddWindowsService();
if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    builder.Services.AddSystemd();

builder.Services.AddHostedService<Worker>();
var host = builder.Build();
host.Run();