using ipfixCollector;
using NLog;

namespace collectorService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    private readonly CollectorServer ipfixCollector;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _logger.LogInformation("worker started"); 
        ipfixCollector = new CollectorServer(false,Environment.UserInteractive);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await ipfixCollector.Stop();
        
        await base.StopAsync(cancellationToken);
        LogManager.Shutdown();
        _logger.LogInformation("worker stopped");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("worker executed");
        await ipfixCollector.Start();
    }
}