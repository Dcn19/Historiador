using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Threading.Tasks;
using CoreServices;
using CoreServices.Services;

namespace MyOpcUaApi.Controllers
{
    [ApiController]
    [Route("api/monitoramento")]
    public class MonitoramentoController : ControllerBase
    {
        private readonly ApplicationManager _appManager;

        public MonitoramentoController(ApplicationManager appManager)
        {
            _appManager = appManager;
        }

        [HttpPost("start")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MonitoramentoResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponse007))]
        public async Task<IActionResult> StartMonitoramento()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // 1️⃣ Obtém a connection string atual
                string connectionStringBase = _appManager.GetCurrentDatabaseConnectionString();
                if (string.IsNullOrWhiteSpace(connectionStringBase))
                {
                    return StatusCode(500, new ErrorResponse007
                    {
                        status = 500,
                        message = "Nenhuma conexão ativa com o banco. Conecte-se antes de iniciar o monitoramento."
                    });
                }

                // 2️⃣ Lista os bancos disponíveis usando essa conexão
                var databases = _appManager.ListAvailableDatabases();
                if (databases == null || databases.Count == 0)
                {
                    return StatusCode(500, new ErrorResponse007
                    {
                        status = 500,
                        message = "Nenhum banco de dados encontrado para o usuário atual."
                    });
                }

                // 3️⃣ Tenta se conectar a todos os bancos encontrados
                foreach (var dbName in databases)
                {
                    var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionStringBase)
                    {
                        Database = dbName
                    };

                    bool adicionado = _appManager.AddDatabaseConnection(csb.ToString());
                    Console.WriteLine($"[DEBUG] Adicionando conexão com banco '{dbName}': {adicionado}");
                }

                // 🔧 Garante que o MonitoramentoService foi instanciado
                var _ = HttpContext.RequestServices.GetService<MonitoramentoService>();

                // 4️⃣ Inicia o monitoramento
                await _appManager.StartMonitoring();

                stopwatch.Stop();

                return Ok(new MonitoramentoResponse
                {
                    status = 200,
                    message = "Monitoramento iniciado com sucesso para todos os bancos disponíveis.",
                    responseTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse007
                {
                    status = 500,
                    message = "Erro ao iniciar monitoramento.",
                    error = ex.Message
                });
            }
        }


        [HttpPost("stop")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MonitoramentoResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponse007))]
        public IActionResult StopMonitoramento()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _appManager.StopMonitoring();
                stopwatch.Stop();
                return Ok(new MonitoramentoResponse
                {
                    status = 200,
                    message = "Monitoramento interrompido com sucesso.",
                    responseTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse007
                {
                    status = 500,
                    message = "Erro ao parar monitoramento.",
                    error = ex.Message
                });
            }
        }

        [HttpGet("status")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StatusResponse00))]
        public IActionResult GetMonitoramentoStatus()
        {
            bool isActive = _appManager.IsMonitoringActive();
            string statusMessage = isActive ? "Monitoramento em execução" : "Monitoramento cancelado";

            return Ok(new StatusResponse00
            {
                status = 200,
                message = statusMessage,
                monitoring = isActive
            });
        }

    }

    public class MonitoramentoResponse
    {
        public int status { get; set; }
        public string message { get; set; }
        public long responseTimeMs { get; set; }
    }

    public class StatusResponse00
    {
        public int status { get; set; }
        public string message { get; set; }
        public bool monitoring { get; set; }
    }

    public class ErrorResponse007
    {
        public int status { get; set; }
        public string message { get; set; }
        public string error { get; set; }
    }
}
