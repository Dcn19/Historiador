using CoreServices;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

public class MonitoramentoBackgroundService : BackgroundService
{
    private readonly MonitoramentoStatusManager _statusManager;
    private readonly ApplicationManager _appManager;

    private bool _monitoramentoAtivo = false;

    public MonitoramentoBackgroundService(
        MonitoramentoStatusManager statusManager,
        ApplicationManager appManager)
    {
        _statusManager = statusManager;
        _appManager = appManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[INFO] Serviço de monitoramento em segundo plano iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_statusManager.PodeMonitorar)
            {
                if (!_monitoramentoAtivo)
                {
                    Console.WriteLine("[INFO] ✅ Requisitos OK. Iniciando monitoramento automático...");
                    _monitoramentoAtivo = true;
                    await _appManager.StartMonitoring(); // Usa o que você já tem
                }
            }
            else
            {
                if (_monitoramentoAtivo)
                {
                    Console.WriteLine("[INFO] 🚫 Monitoramento pausado. Requisitos não atendidos.");
                    _monitoramentoAtivo = false;
                    _appManager.StopMonitoring(); // Você pode implementar isso se quiser
                }
            }

            await Task.Delay(2000, stoppingToken); // Verifica a cada 2 segundos
        }

        Console.WriteLine("[INFO] Serviço de monitoramento finalizado.");
    }
}

