using CoreServices.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyOpcUaApi.Services
{
    public class MonitoramentoControllerService
    {
        private readonly MonitoramentoService _monitoramentoService;

        public MonitoramentoControllerService(MonitoramentoService monitoramentoService)
        {
            _monitoramentoService = monitoramentoService;
        }

        public async Task StartMonitoringAsync(List<DatabaseManager> databaseManagers)
        {
            await _monitoramentoService.IniciarMonitoramentoAsync(databaseManagers);
        }

        public async Task RestartMonitoringAsync(List<DatabaseManager> databaseManagers)
        {
            await _monitoramentoService.ReiniciarMonitoramento(databaseManagers);
        }

        public void StopMonitoring()
        {
            _monitoramentoService.CancelarMonitoramento();
        }
    }
}
