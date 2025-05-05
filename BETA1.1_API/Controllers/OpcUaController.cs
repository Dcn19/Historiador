using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using CoreServices;
using MyOpcUaApi.Models;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using BETA1._1_API.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using CoreServices.Services;
using static CoreServices.Services.OpcUaClient;
using CoreServices.Models;
using System.Text.RegularExpressions;


namespace MyOpcUaApi.Controllers
{
    [ApiController]
    [Route("api/opcua")]
    public class OpcUaController : ControllerBase
    {
        private readonly ApplicationManager _appManager;
        private readonly IMemoryCache _memoryCache;
        private readonly MonitoramentoStatusManager _statusManager;
        private readonly DatabaseManager _databaseManager;

        public OpcUaController(ApplicationManager appManager, IMemoryCache memoryCache, MonitoramentoStatusManager statusManager, DatabaseManager databaseManager)
        {
            _appManager = appManager;
            _memoryCache = memoryCache;
            _statusManager = statusManager;
            _databaseManager = databaseManager;
        }



        [HttpPost("connect")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConnectResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseconnect))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseconnect))]
        public async Task<IActionResult> ConnectToOpcUa(
    [FromQuery] string? serverUrl,
    [FromBody] ConnectRequest? request)
        {
            serverUrl ??= request?.ServerUrl;

            if (string.IsNullOrEmpty(serverUrl))
            {
                return BadRequest(new ErrorResponseconnect
                {
                    status = 400,
                    message = "A URL do servidor não pode ser vazia. Envie via query string ou no corpo da requisição.",
                    exampleRequest = new { serverUrl = "opc.tcp://192.168.1.1:4840" }
                });
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                bool isConnected = await _appManager.ConnectToOpcUaServer(serverUrl);
                stopwatch.Stop();

                if (isConnected)
                {
                    // 🔹 Informa que o CLP foi conectado com sucesso
                    //_statusManager.SetClpConectado(true);

                    return Ok(new ConnectResponse
                    {
                        status = 200,
                        message = "Conectado ao servidor OPC UA com sucesso.",
                        serverUrl = serverUrl,
                        responseTimeMs = stopwatch.ElapsedMilliseconds
                    });
                }
                else
                {
                    return StatusCode(500, new ErrorResponseconnect
                    {
                        status = 500,
                        message = "Falha ao conectar ao servidor OPC UA. Verifique se o servidor está ativo e acessível.",
                        serverUrl = serverUrl,
                        exampleRequest = new { serverUrl = "opc.tcp://192.168.1.1:4840" }
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponseconnect
                {
                    status = 500,
                    message = "Erro interno ao tentar conectar ao servidor OPC UA.",
                    error = ex.Message,
                    serverUrl = serverUrl,
                    exampleRequest = new { serverUrl = "opc.tcp://192.168.1.1:4840" }
                });
            }
        }





        [HttpGet("last-connection")]
        public IActionResult GetLastOpcUaConnection()
        {
            try
            {
                string connectionString = _appManager.GetCurrentDatabaseConnectionString();
                string lastUrl;

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    lastUrl = _appManager.GetDatabaseManager()?.GetLastOpcUaConnection(connectionString);
                    if (string.IsNullOrEmpty(lastUrl))
                        lastUrl = _appManager.GetLastConnectedServerUrl(); // fallback para variável interna
                }
                else
                {
                    lastUrl = _appManager.GetLastConnectedServerUrl();
                }

                if (string.IsNullOrEmpty(lastUrl))
                {
                    return NotFound(new
                    {
                        status = 404,
                        message = "Nenhuma conexão OPC UA encontrada."
                    });
                }

                return Ok(new
                {
                    status = 200,
                    message = "Última conexão OPC UA recuperada com sucesso.",
                    serverUrl = lastUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    status = 500,
                    message = "Erro ao recuperar a última conexão OPC UA.",
                    error = ex.Message
                });
            }
        }




        /// <summary>
        /// Resposta de sucesso da conexão OPC UA.
        /// </summary>
        public class ConnectResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public long responseTimeMs { get; set; }
        }

        /// <summary>
        /// Modelo de erro para respostas padronizadas.
        /// </summary>
        public class ErrorResponseconnect
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string serverUrl { get; set; }
            public object exampleRequest { get; set; }
        }



        [HttpPost("disconnect")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DisconnectResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponse))]
        public IActionResult Disconnect()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Obtém a URL do último servidor conectado
                string lastConnectedServer = _appManager.GetLastConnectedServerUrl();

                _appManager.DisconnectOpcUaServer();
                stopwatch.Stop();

                return Ok(new DisconnectResponse
                {
                    status = 200,
                    message = "Desconectado do servidor OPC UA com sucesso.",
                    serverUrl = lastConnectedServer,
                    responseTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    status = 500,
                    message = "Erro ao tentar desconectar do servidor OPC UA.",
                    error = ex.Message
                });
            }
        }



        /// <summary>
        /// Resposta de sucesso ao desconectar.
        /// </summary>
        public class DisconnectResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public long responseTimeMs { get; set; }
        }


        /// <summary>
        /// Modelo de erro para respostas padronizadas.
        /// </summary>
        public class ErrorResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
        }



        [HttpGet("nodes/{nodeId}/variables")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NodeVariablesResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponse))]
        public IActionResult GetNodeVariables([FromRoute] string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return BadRequest(new ErrorResponsevariables
                {
                    status = 400,
                    message = "O NodeId não pode ser vazio. Informe um ID válido para obter as variáveis.",
                    exampleRequest = new { nodeId = "ns=2;s=Tag1" }
                });
            }

            try
            {
                var serverUrl = _appManager.GetLastConnectedServerUrl();
                var variables = _appManager.GetOpcUaNodeVariables(nodeId);

                if (variables == null || !variables.Any())
                {
                    return NotFound(new ErrorResponsevariables
                    {
                        status = 404,
                        message = $"Nenhuma variável encontrada para o NodeId: {nodeId}.",
                        serverUrl = serverUrl
                    });
                }

                // 🔹 Armazena o último NodeId no cache por 5 minutos
                _memoryCache.Set("current_nodeId", nodeId, TimeSpan.FromMinutes(5));

                return Ok(new NodeVariablesResponse
                {
                    status = 200,
                    message = "Variáveis obtidas com sucesso.",
                    serverUrl = serverUrl,
                    nodeId = nodeId,
                    variables = variables
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponsevariables
                {
                    status = 500,
                    message = "Erro ao tentar obter as variáveis do NodeId.",
                    error = ex.Message
                });
            }
        }



        [HttpGet("nodes/{nodeId}/children")]
        public IActionResult GetNodeChildren([FromRoute] string nodeId, [FromQuery] bool? refresh = false)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "O NodeId não pode ser vazio.",
                    exampleRequest = new { nodeId = "ns=3;s=\"IhmPremix\"" }
                });
            }

            try
            {
                var serverUrl = _appManager.GetLastConnectedServerUrl();
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Nenhum servidor OPC UA está conectado."
                    });
                }

                // 🔒 Verifica se há uma sessão ativa com o CLP
                if (!_appManager.IsOpcUaConnected())
                {
                    return StatusCode(503, new
                    {
                        status = 503,
                        message = "Não há conexão ativa com o CLP. Conecte-se antes de buscar os nós."
                    });
                }

                // 🔒 Normaliza a chave de cache
                string cacheKey = $"opcua_children_{nodeId.Trim().Replace("\"", "")}";

                // 🧠 Usa o cache se o refresh for false ou não informado
                if (refresh == false && _memoryCache.TryGetValue(cacheKey, out List<OpcUaNodeDto>? cachedNodes))
                {
                    Console.WriteLine($"[CACHE] Retornando filhos de {nodeId} do cache.");
                    return Ok(new
                    {
                        status = 200,
                        message = "Filhos obtidos do cache.",
                        serverUrl,
                        nodeId,
                        children = cachedNodes
                    });
                }

                // 🔁 Caso refresh seja true ou não tenha nada salvo
                Console.WriteLine($"[BUSCA] Buscando filhos de {nodeId} com profundidade 2...");
                var children = _appManager.GetOpcUaNodeHierarchy(nodeId, 2);

                if (children == null || !children.Any())
                {
                    return NotFound(new
                    {
                        status = 404,
                        message = $"Nenhum filho encontrado para o NodeId: {nodeId}.",
                        serverUrl
                    });
                }

                // 🧠 Salva os dados com SlidingExpiration de 1 hora
                _memoryCache.Set(cacheKey, children, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(1)
                });

                return Ok(new
                {
                    status = 200,
                    message = "Filhos obtidos com sucesso do CLP.",
                    serverUrl,
                    nodeId,
                    children
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    status = 500,
                    message = "Erro ao buscar filhos do NodeId.",
                    error = ex.Message
                });
            }
        }





        [HttpPost("nodes/variables/select")]
        public async Task<IActionResult> SelectNodeVariables([FromBody] TagSelectionRequest request, [FromServices] DatabaseOrchestratorService orchestrator)
        {
            if (request?.SelectedTags == null || !request.SelectedTags.Any())
            {
                return BadRequest(new ErrorResponse
                {
                    status = 400,
                    message = "As tags selecionadas não podem ser nulas ou vazias."
                });
            }

            try
            {
                var selectedTagObjects = new List<TagInfoRequest>();

                foreach (var nodeId in request.SelectedTags)
                {
                    var variable = _appManager.GetOpcUaVariableDetails(nodeId);

                    if (variable == null)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            status = 400,
                            message = $"Não foi possível encontrar detalhes para a tag: {nodeId}"
                        });
                    }

                    var nomeNormalizado = _appManager.GetDatabaseManager().NormalizeTagName(nodeId);

                    selectedTagObjects.Add(new TagInfoRequest(
                        id: 0,
                        nodeId: variable.NodeId,
                        nomeNormalizado: nomeNormalizado,
                        tipoDado: variable.DataType,
                        nomeTabelaOrigem: request.TableName ?? "tabela_padrao",
                        displayName: variable.DisplayName
                    ));
                }

                _memoryCache.Set("selected_tags", selectedTagObjects, TimeSpan.FromMinutes(5));

                if (!string.IsNullOrWhiteSpace(request.TableName))
                {
                    _memoryCache.Set("selected_tableName", request.TableName.Trim(), TimeSpan.FromMinutes(5));
                }

                // Agora só cria a tabela, sem inserir dados
                var createResponse = await orchestrator.ExecuteCreateTableOnlyAsync();

                return Ok(new
                {
                    status = 200,
                    message = "Tabela criada com sucesso com as colunas correspondentes às tags selecionadas.",
                    selectedTags = selectedTagObjects,
                    tableName = request.TableName,
                    result = createResponse
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    status = 500,
                    message = "Erro ao tentar criar a tabela com as tags selecionadas.",
                    error = ex.Message
                });
            }
        }

        [HttpPost("database/equipamento/adicionar")]
        public async Task<IActionResult> AdicionarEquipamento([FromBody] NovoEquipamentoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TableName) || string.IsNullOrWhiteSpace(request.NovoPrefixo) || string.IsNullOrWhiteSpace(request.NomeEquipamento))
            {
                return BadRequest(new ErrorResponse
                {
                    status = 400,
                    message = "Todos os campos (TableName, NovoPrefixo, NomeEquipamento) são obrigatórios."
                });
            }

            string connectionString = _appManager.GetCurrentDatabaseConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return StatusCode(500, new ErrorResponse
                {
                    status = 500,
                    message = "Nenhuma conexão ativa com o banco de dados."
                });
            }

            var tagColunas = _databaseManager.GetTagColumnsFromTable(request.TableName, connectionString);

            if (tagColunas == null || !tagColunas.Any())
            {
                return NotFound(new ErrorResponse
                {
                    status = 404,
                    message = $"Nenhuma tag foi encontrada na tabela '{request.TableName}'."
                });
            }

            string tagExemplo = tagColunas.First();
            var match = Regex.Match(tagExemplo, "s=\\\"(.*?)\\\"");
            if (!match.Success)
            {
                return StatusCode(500, new ErrorResponse
                {
                    status = 500,
                    message = "Não foi possível detectar o prefixo da tag de exemplo."
                });
            }

            string trechoAntigo = match.Groups[1].Value;

            var valoresParaInserir = new Dictionary<string, object>
    {
        { "equipamento", request.NomeEquipamento }
    };

            var valoresParaRetorno = new Dictionary<string, object>
    {
        { "equipamento", request.NomeEquipamento }
    };

            foreach (var coluna in tagColunas)
            {
                if (!coluna.Contains("s=\"") || coluna.EndsWith("_valor") || coluna.EndsWith("_displayname") || coluna.EndsWith("_tipo"))
                    continue;

                string novaTag = coluna.Replace(trechoAntigo, request.NovoPrefixo);

                try
                {
                    var valorLido = await _appManager.ReadOpcUaTag(novaTag);
                    var valorConvertido = DatabaseManager.ConvertTagValue(valorLido);
                    var displayName = _appManager.GetOpcUaNodeDisplayName(novaTag);
                    var tipo = valorLido?.GetType().Name ?? "null";

                    Console.WriteLine($"[DEBUG] Tag original da tabela: {coluna}");
                    Console.WriteLine($"[DEBUG] Nova tag gerada: {novaTag}");
                    Console.WriteLine($"[DEBUG] Valor lido do CLP: {valorLido} | Tipo: {tipo}");
                    Console.WriteLine($"[DEBUG] Valor convertido: {valorConvertido}");
                    Console.WriteLine($"[DEBUG] Display Name: {displayName}");

                    string colunaValor = $"{coluna}_valor";
                    string colunaDisplayName = $"{coluna}_displayname";
                    string colunaTipo = $"{coluna}_tipo";

                    valoresParaInserir[coluna] = novaTag;
                    valoresParaInserir[colunaValor] = valorConvertido;
                    valoresParaInserir[colunaDisplayName] = displayName;
                    valoresParaInserir[colunaTipo] = tipo;

                    valoresParaRetorno[coluna] = novaTag;
                    valoresParaRetorno[colunaValor] = valorConvertido;
                    valoresParaRetorno[colunaDisplayName] = displayName;
                    valoresParaRetorno[colunaTipo] = tipo;

                    Console.WriteLine($"[INSERT] {colunaValor} => {valorConvertido}");
                    Console.WriteLine($"[INSERT] {colunaDisplayName} => {displayName}");
                    Console.WriteLine($"[INSERT] {colunaTipo} => {tipo}");
                }
                catch
                {
                    return BadRequest(new ErrorResponse
                    {
                        status = 400,
                        message = $"A tag '{novaTag}' não pôde ser lida ou não existe no CLP."
                    });
                }
            }



            try
            {
                await _databaseManager.InsertRowAsync(request.TableName, valoresParaInserir, connectionString);

                return Ok(new
                {
                    status = 200,
                    message = $"Equipamento '{request.NomeEquipamento}' adicionado com sucesso na tabela '{request.TableName}'.",
                    valores = valoresParaRetorno
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    status = 500,
                    message = "Erro ao inserir o equipamento na tabela.",
                    error = ex.Message
                });
            }
        }


        public class NovoEquipamentoRequest
        {
            public string TableName { get; set; } = string.Empty;
            public string NovoPrefixo { get; set; } = string.Empty;
            public string NomeEquipamento { get; set; } = string.Empty;
        }




        public class OpcUaVariable
        {
            public string NodeId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string DataType { get; set; } = string.Empty;
        }

        /// <summary>
        /// Resposta ao buscar as variáveis de um NodeId.
        /// </summary>
        public class NodeVariablesResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public string nodeId { get; set; }
            public object variables { get; set; }
        }

        /// <summary>
        /// Modelo de erro padronizado.
        /// </summary>
        public class ErrorResponsevariables
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string serverUrl { get; set; }
            public object exampleRequest { get; set; }
        }

        [HttpGet("nodes/children")]
        public IActionResult GetFixedNodeChildrenShallow([FromQuery] int nodeIndex)
        {
            try
            {
                var serverUrl = _appManager.GetLastConnectedServerUrl();
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Nenhum servidor OPC UA está conectado."
                    });
                }

                var allowedNodes = new Dictionary<int, string>
        {
            { 0, "ns=3;s=DataBlocksGlobal" },
            { 1, "ns=3;s=DataBlocksInstance" }
        };

                if (!allowedNodes.TryGetValue(nodeIndex, out var rootNodeId))
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "nodeIndex inválido. Utilize 0 para Global ou 1 para Instance."
                    });
                }

                // ✅ Busca direta apenas os filhos imediatos (sem profundidade)
                var directChildren = _appManager.GetOpcUaClient().GetChildren(rootNodeId);

                return Ok(new
                {
                    status = 200,
                    message = "Filhos obtidos com sucesso (apenas camada 1).",
                    nodeId = rootNodeId,
                    children = directChildren
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    status = 500,
                    message = "Erro ao buscar os filhos do nó.",
                    error = ex.Message
                });
            }
        }




        public class OpcUaChildNode
        {
            public string NodeId { get; set; }
            public string DisplayName { get; set; }
            public bool HasChildren { get; set; }
        }

        public class NodeChildrenResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public List<OpcUaChildNode> children { get; set; }
        }



        /// <summary>
        /// Resposta da hierarquia de nós do servidor OPC UA.
        /// </summary>
        public class NodeHierarchyResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public int depth { get; set; }
            public object nodes { get; set; }
        }

        /// <summary>
        /// Modelo de erro padronizado.
        /// </summary>
        public class ErrorResponsehierarchy
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string serverUrl { get; set; }
            public object exampleRequest { get; set; }
        }



        [HttpGet("nodeType/{nodeId}")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NodeTypeResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponse))]
        [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ErrorResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponse))]
        public IActionResult GetNodeType([FromRoute] string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return BadRequest(new ErrorResponsenodeType
                {
                    status = 400,
                    message = "O NodeId não pode ser vazio. Informe um ID válido para obter o tipo.",
                    exampleRequest = new { nodeId = "ns=2;s=Tag1" }
                });
            }

            try
            {
                var serverUrl = _appManager.GetLastConnectedServerUrl();
                var nodeType = _appManager.GetOpcUaNodeType(nodeId);

                if (nodeType == null)
                {
                    return NotFound(new ErrorResponsenodeType
                    {
                        status = 404,
                        message = $"Nenhum tipo encontrado para o NodeId: {nodeId}.",
                        serverUrl = serverUrl
                    });
                }

                return Ok(new NodeTypeResponse
                {
                    status = 200,
                    message = "Tipo de nó obtido com sucesso.",
                    serverUrl = serverUrl,
                    nodeId = nodeId,
                    nodeType = nodeType
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponsenodeType
                {
                    status = 500,
                    message = "Erro ao tentar obter o tipo do NodeId.",
                    error = ex.Message
                });
            }
        }


        /// <summary>
        /// Resposta ao buscar o tipo de um NodeId.
        /// </summary>
        public class NodeTypeResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public string nodeId { get; set; }
            public string nodeType { get; set; }
        }

        /// <summary>
        /// Modelo de erro padronizado.
        /// </summary>
        public class ErrorResponsenodeType
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string serverUrl { get; set; }
            public object exampleRequest { get; set; }
        }



        [HttpGet("status")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StatusResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponse))]
        public IActionResult GetOpcUaStatus()
        {
            try
            {
                var isConnected = _appManager.IsOpcUaConnected();
                var lastConnectedServer = _appManager.GetLastConnectedServerUrl(); // Obtém a última URL conectada

                return Ok(new StatusResponse
                {
                    status = 200,
                    connected = isConnected,
                    serverUrl = isConnected ? lastConnectedServer : "Nenhum servidor conectado",
                    message = isConnected ? "O servidor OPC UA está conectado." : "Nenhum servidor OPC UA está conectado."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponsestatus
                {
                    status = 500,
                    message = "Erro ao obter o status da conexão OPC UA.",
                    error = ex.Message
                });
            }
        }


        /// <summary>
        /// Resposta do status da conexão OPC UA.
        /// </summary>
        public class StatusResponse
        {
            public int status { get; set; }
            public bool connected { get; set; }
            public string serverUrl { get; set; }
            public string message { get; set; }
        }

        /// <summary>
        /// Modelo de erro padronizado.
        /// </summary>
        public class ErrorResponsestatus
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
        }



        [HttpGet("read/{nodeId}")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReadTagResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponse))]
        [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ErrorResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponse))]
        public async Task<IActionResult> ReadTagValue([FromRoute] string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return BadRequest(new ErrorResponseread
                {
                    status = 400,
                    message = "O NodeId não pode ser vazio. Informe um ID válido para ler o valor.",
                    exampleRequest = new { nodeId = "ns=2;s=Tag1" }
                });
            }

            try
            {
                var serverUrl = _appManager.GetLastConnectedServerUrl();
                var value = await _appManager.ReadOpcUaTag(nodeId);

                if (value == null)
                {
                    return NotFound(new ErrorResponseread
                    {
                        status = 404,
                        message = $"Nenhum valor encontrado para o NodeId: {nodeId}.",
                        serverUrl = serverUrl
                    });
                }

                return Ok(new ReadTagResponse
                {
                    status = 200,
                    message = "Valor da tag lido com sucesso.",
                    serverUrl = serverUrl,
                    nodeId = nodeId,
                    value = value
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponseread
                {
                    status = 500,
                    message = "Erro ao tentar ler o valor da tag.",
                    error = ex.Message
                });
            }
        }


        /// <summary>
        /// Resposta ao ler o valor de uma tag OPC UA.
        /// </summary>
        public class ReadTagResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public string nodeId { get; set; }
            public object value { get; set; }
        }

        /// <summary>
        /// Modelo de erro padronizado.
        /// </summary>
        public class ErrorResponseread
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string serverUrl { get; set; }
            public object exampleRequest { get; set; }
        }



        [HttpPost("write")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WriteTagResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponsewrite))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponsewrite))]
        public async Task<IActionResult> WriteTagValue(
    [FromQuery] string? nodeId,  // Permite envio via query string
    [FromQuery] string? value,   // Permite envio via query string (como string)
    [FromBody] WriteRequest? request) // Permite envio via JSON no corpo
        {
            // 🔹 Se o parâmetro não veio via query, tenta pegar do corpo da requisição
            nodeId ??= request?.NodeId;
            object finalValue = request?.Value ?? value; // Prioriza o JSON, mas mantém query

            if (string.IsNullOrEmpty(nodeId))
            {
                return BadRequest(new ErrorResponsewrite
                {
                    status = 400,
                    message = "O NodeId não pode ser vazio. Informe um ID válido para escrever um valor.",
                    exampleRequest = new { nodeId = "ns=2;s=Tag1", value = 100 }
                });
            }

            if (finalValue == null)
            {
                return BadRequest(new ErrorResponsewrite
                {
                    status = 400,
                    message = "O valor a ser escrito não pode ser nulo.",
                    exampleRequest = new { nodeId = "ns=2;s=Tag1", value = 100 }
                });
            }

            try
            {
                var serverUrl = _appManager.GetLastConnectedServerUrl();

                // 🔹 Converte o valor corretamente SE vier da Query String
                if (value != null)
                {
                    finalValue = TryConvertStringValue(value);
                }

                bool success = await _appManager.WriteOpcUaTag(nodeId, finalValue);

                if (!success)
                {
                    return StatusCode(500, new ErrorResponsewrite
                    {
                        status = 500,
                        message = $"Erro ao escrever o valor '{finalValue}' no NodeId '{nodeId}'.",
                        serverUrl = serverUrl
                    });
                }

                return Ok(new WriteTagResponse
                {
                    status = 200,
                    message = "Valor escrito com sucesso na tag OPC UA.",
                    serverUrl = serverUrl,
                    nodeId = nodeId,
                    value = finalValue
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponsewrite
                {
                    status = 500,
                    message = "Erro ao tentar escrever o valor na tag OPC UA.",
                    error = ex.Message
                });
            }
        }


        private object TryConvertStringValue(string value)
        {
            // Se o valor for um número inteiro, converte
            if (int.TryParse(value, out int intValue))
                return intValue;

            // Se o valor for um número decimal, converte
            if (double.TryParse(value, out double doubleValue))
                return doubleValue;

            // Se o valor for um booleano (true/false), converte
            if (bool.TryParse(value, out bool boolValue))
                return boolValue;

            // Se não for nenhum desses, mantém como string
            return value;
        }



        /// <summary>
        /// Requisição para escrita de uma tag OPC UA.
        /// </summary>
        public class WriteRequest
        {
            public string NodeId { get; set; }
            public object Value { get; set; }
        }

        /// <summary>
        /// Resposta ao escrever um valor em uma tag OPC UA.
        /// </summary>
        public class WriteTagResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public string nodeId { get; set; }
            public object value { get; set; }
        }

        /// <summary>
        /// Modelo de erro padronizado.
        /// </summary>
        public class ErrorResponsewrite
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string serverUrl { get; set; }
            public object exampleRequest { get; set; }
        }



        [HttpPost("writeWithCompare")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WriteCompareResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponsewrite1))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponsewrite1))]
        public async Task<IActionResult> WriteTagWithCompare(
    [FromQuery] string? nodeId,
    [FromQuery] string? value,
    [FromQuery] string? comparator,
    [FromBody] WriteCompareRequest? request)
        {
            // 🔹 Se os valores não vieram via query, tenta pegar do corpo da requisição
            nodeId ??= request?.NodeId;
            object finalValue = request?.Value ?? value;
            comparator ??= request?.Comparator;

            if (string.IsNullOrEmpty(nodeId))
            {
                return BadRequest(new ErrorResponsewrite1
                {
                    status = 400,
                    message = "O NodeId não pode ser vazio.",
                    exampleRequest = new { nodeId = "ns=2;s=Tag1", value = 100, comparator = ">" }
                });
            }

            if (finalValue == null)
            {
                return BadRequest(new ErrorResponsewrite1
                {
                    status = 400,
                    message = "O valor a ser escrito não pode ser nulo.",
                    exampleRequest = new { nodeId = "ns=2;s=Tag1", value = 100, comparator = ">" }
                });
            }

            try
            {
                var serverUrl = _appManager.GetLastConnectedServerUrl();

                // 🔹 Converte o valor corretamente SE vier da Query String
                if (value != null)
                {
                    finalValue = TryConvertStringValue(value);
                }

                bool success = await _appManager.WriteOpcUaTagWithValidation(nodeId, finalValue, comparator);

                if (!success)
                {
                    return StatusCode(500, new ErrorResponsewrite1
                    {
                        status = 500,
                        message = $"Erro ao validar e escrever o valor '{finalValue}' no NodeId '{nodeId}'.",
                        serverUrl = serverUrl
                    });
                }

                return Ok(new WriteCompareResponse
                {
                    status = 200,
                    message = "Valor validado e escrito com sucesso.",
                    serverUrl = serverUrl,
                    nodeId = nodeId,
                    value = finalValue,
                    comparator = comparator
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponsewrite1
                {
                    status = 500,
                    message = "Erro ao tentar validar e escrever o valor na tag OPC UA.",
                    error = ex.Message
                });
            }
        }


        public class WriteCompareResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string serverUrl { get; set; }
            public string nodeId { get; set; }
            public object value { get; set; }
            public string comparator { get; set; }
        }

        public class ErrorResponsewrite1
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string serverUrl { get; set; }
            public object exampleRequest { get; set; }
        }


    }

    public class ConnectRequest
    {
        [Required]
        public string ServerUrl { get; set; }

        public int Timeout { get; set; } = 5000; // Timeout opcional padrão
    }

    public class WriteRequest
    {
        [Required]
        public string NodeId { get; set; }

        [Required]
        public object Value { get; set; }
    }

    public class WriteCompareRequest : WriteRequest
    {
        public string Comparator { get; set; } // Exemplo: ">", "<", "="
    }
}
