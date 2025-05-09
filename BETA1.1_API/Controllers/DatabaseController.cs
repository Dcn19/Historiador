using Microsoft.AspNetCore.Mvc;
using CoreServices.Services;
using System.Collections.Generic;
using CoreServices.Models;
using CoreServices;
using MyOpcUaApi.Models;
using Npgsql;
using static MyOpcUaApi.Controllers.OpcUaController;
using Microsoft.Extensions.Caching.Memory;
using ApiTagInfo = MyOpcUaApi.Models.TagInfoRequest;
using System.Text.RegularExpressions;


namespace MyOpcUaApi.Controllers
{
    [ApiController]
    [Route("api/database")]
    public class DatabaseController : ControllerBase
    {
        private readonly DatabaseManager _databaseManager;
        private readonly ApplicationManager _appManager;
        private readonly IMemoryCache _memoryCache;

        private readonly MonitoramentoStatusManager _statusManager;


        public DatabaseController(DatabaseManager databaseManager, ApplicationManager appManager, IMemoryCache memoryCache, MonitoramentoStatusManager statusManager)
        {
            if (databaseManager == null) throw new ArgumentNullException(nameof(databaseManager));
            if (appManager == null) throw new ArgumentNullException(nameof(appManager));
            if (memoryCache == null) throw new ArgumentNullException(nameof(memoryCache));

            Console.WriteLine("[DEBUG] DatabaseController foi instanciado com sucesso!");

            _databaseManager = databaseManager;
            _appManager = appManager;
            _memoryCache = memoryCache;
            _statusManager = statusManager;
        }


        [HttpPost("createAndInsert")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CreateInsertResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseDB))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseDB))]
        public async Task<IActionResult> CreateAndInsert()
        {
            try
            {

                if (!_memoryCache.TryGetValue("selected_tags", out List<TagInfoRequest>? selectedTags) || selectedTags == null || !selectedTags.Any())
                {
                    return BadRequest(new ErrorResponseDB
                    {
                        status = 400,
                        message = "Nenhuma tag selecionada encontrada ou a seleção expirou. Selecione as tags novamente."
                    });
                }

                if (!_memoryCache.TryGetValue("selected_tableName", out string? tableName) || string.IsNullOrWhiteSpace(tableName))
                {
                    return BadRequest(new ErrorResponseDB
                    {
                        status = 400,
                        message = "Nome da tabela não encontrado no cache. Informe o nome ao selecionar as tags."
                    });
                }

                string connectionString = _appManager.GetCurrentDatabaseConnectionString();
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return BadRequest(new ErrorResponseDB
                    {
                        status = 400,
                        message = "Nenhuma conexão ativa com o banco de dados. Conecte-se antes de criar uma tabela."
                    });
                }

                _databaseManager.EnsureTagsMonitoramentoTableExists(connectionString);
                string formattedTableName = _databaseManager.NormalizeTagName(tableName);

                // 🔹 Cria a lista de TagInfoRequest com DisplayName atualizado
                var tags = selectedTags.Select(tag =>
                {
                    string normalizedTagName = _databaseManager.NormalizeTagName(tag.NodeId); // só se quiser reforçar
                    string? displayName = _appManager.GetOpcUaNodeDisplayName(tag.NodeId); // Atualiza caso tenha mudado

                    return new MyOpcUaApi.Models.TagInfoRequest(
                        id: 0,
                        nodeId: tag.NodeId,
                        nomeNormalizado: normalizedTagName,
                        tipoDado: tag.TipoDado,
                        nomeTabelaOrigem: formattedTableName,
                        displayName: displayName
                    );
                }).ToList();


                // 🔹 Converte para TagInfo da camada Core para criar a tabela
                var tagsCore = tags.Select(tag => new CoreServices.Models.TagInfo(
                    id: tag.Id,
                    nodeIdString: tag.NodeId,
                    tagName: tag.NomeNormalizado,
                    postgreType: _databaseManager.MapOpcUaDataTypeToPostgreSql(tag.TipoDado ?? "text"),
                    nomeTabelaOrigem: tag.NomeTabelaOrigem
                )).ToList();

                if (!_databaseManager.TableExists(formattedTableName, connectionString))
                {
                    _databaseManager.CreateTable(formattedTableName, tagsCore, connectionString);
                }

                // 🔹 Inserção na tabela tags_monitoramento
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    foreach (var tag in tags)
                    {
                        string insertTagQuery = @"
INSERT INTO tags_monitoramento (node_id, nome_normalizado, nome_tabela_origem, tipo_dado, display_name)
VALUES (@node_id, @nome_normalizado, @nome_tabela_origem, @tipo_dado, @display_name)
ON CONFLICT (node_id) DO UPDATE 
SET tipo_dado = EXCLUDED.tipo_dado,
    display_name = EXCLUDED.display_name;";

                        using var insertCmd = new NpgsqlCommand(insertTagQuery, conn);
                        insertCmd.Parameters.AddWithValue("@node_id", tag.NodeId);
                        insertCmd.Parameters.AddWithValue("@nome_normalizado", tag.NomeNormalizado);
                        insertCmd.Parameters.AddWithValue("@nome_tabela_origem", formattedTableName);

                        // Fallback: se tipoDado for nulo, define como "text"
                        insertCmd.Parameters.AddWithValue("@tipo_dado", tag.TipoDado ?? "text");

                        insertCmd.Parameters.AddWithValue("@display_name", (object?)tag.DisplayName ?? DBNull.Value);

                        Console.WriteLine($"[DEBUG] Inserindo tag_monitoramento: {tag.NomeNormalizado} | DisplayName: {tag.DisplayName} | TipoDado: {tag.TipoDado ?? "text"}");
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }


                // 🔹 Leitura de valores atuais e inserção na nova tabela
                var tagValues = new Dictionary<string, object>();
                foreach (var tag in tags)
                {
                    try
                    {
                        var rawValue = await _appManager.ReadOpcUaTag(tag.NodeId);
                        var convertedValue = DatabaseManager.ConvertTagValue(rawValue);
                        tagValues[tag.NomeNormalizado] = convertedValue;

                        Console.WriteLine($"[INFO] Valor da tag '{tag.NomeNormalizado}': {convertedValue}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERRO] Falha ao ler a tag '{tag.NomeNormalizado}': {ex.Message}");
                    }
                }

                await _databaseManager.SaveTagValuesAsync(formattedTableName, tagValues, DateTime.UtcNow, connectionString);

                // 🧹 Limpa o cache para evitar mistura de tags em futuras criações
                _memoryCache.Remove("selected_tags");
                _memoryCache.Remove("selected_tableName");
                _memoryCache.Remove("current_nodeId");

                return Ok(new CreateInsertResponse
                {
                    status = 200,
                    message = $"Tabela '{formattedTableName}' criada (se necessário) e valores reais inseridos com sucesso.",
                    tableName = formattedTableName,
                    insertedTags = tagValues
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponseDB
                {
                    status = 500,
                    message = "Erro interno ao processar a criação e inserção de dados.",
                    error = ex.Message
                });
            }
        }


        public class ReplicacaoTagsRequest
        {
            public int Inicio { get; set; }
            public int Fim { get; set; }
        }

        [HttpPost("replicar-tabelas")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseDB))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseDB))]
        public async Task<IActionResult> ReplicarTabelasPorEquipamento([FromBody] ReplicacaoTagsRequest request)
        {
            try
            {
                if (!_memoryCache.TryGetValue("selected_tags", out List<TagInfoRequest>? baseTags) || baseTags == null || !baseTags.Any())
                    return BadRequest(new ErrorResponseDB { status = 400, message = "Nenhuma tag selecionada encontrada no cache." });

                if (!_memoryCache.TryGetValue("selected_tableName", out string? tabelaBase) || string.IsNullOrWhiteSpace(tabelaBase))
                    return BadRequest(new ErrorResponseDB { status = 400, message = "Nome da tabela não encontrado no cache." });

                string connectionString = _appManager.GetCurrentDatabaseConnectionString();
                if (string.IsNullOrWhiteSpace(connectionString))
                    return BadRequest(new ErrorResponseDB { status = 400, message = "Nenhuma conexão ativa com o banco de dados." });

                _databaseManager.EnsureTagsMonitoramentoTableExists(connectionString);

                // 🔍 Extrai prefixo + número do equipamento base, ex: "RO03"
                string? equipamentoOriginal = baseTags.Select(t => t.NodeId)
                    .Select(id => Regex.Match(id ?? string.Empty, @"001-(\D+\d+)-Acionamento").Groups[1].Value)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

                if (string.IsNullOrWhiteSpace(equipamentoOriginal) || equipamentoOriginal.Length < 3)
                    return BadRequest(new ErrorResponseDB { status = 400, message = "Não foi possível extrair o código do equipamento base da tag." });

                string prefixo = Regex.Match(equipamentoOriginal, "(\\D+)").Groups[1].Value;

                var tabelasCriadas = new List<string>();

                for (int i = request.Inicio; i <= request.Fim; i++)
                {
                    string novoCodigo = prefixo + i.ToString("D2");
                    string tabelaFormatada = _databaseManager.NormalizeTagName($"{tabelaBase}_{novoCodigo.ToLower()}");

                    var tagsAtualizadas = baseTags.Select(tag =>
                    {
                        string novoNodeId = tag.NodeId.Replace(equipamentoOriginal, novoCodigo);
                        string nomeNormalizado = _databaseManager.NormalizeTagName(novoNodeId); // <- Corrigido!
                        string? displayName = _appManager.GetOpcUaNodeDisplayName(novoNodeId);

                        return new TagInfoRequest(
                            id: 0,
                            nodeId: novoNodeId,
                            nomeNormalizado: nomeNormalizado,
                            tipoDado: tag.TipoDado,
                            nomeTabelaOrigem: tabelaFormatada,
                            displayName: displayName
                        );
                    }).ToList();

                    var tagsCore = tagsAtualizadas.Select(tag => new CoreServices.Models.TagInfo(
                        id: tag.Id,
                        nodeIdString: tag.NodeId,
                        tagName: tag.NomeNormalizado,
                        postgreType: _databaseManager.MapOpcUaDataTypeToPostgreSql(tag.TipoDado ?? "text"),
                        nomeTabelaOrigem: tag.NomeTabelaOrigem)).ToList();

                    if (!_databaseManager.TableExists(tabelaFormatada, connectionString))
                    {
                        _databaseManager.CreateTable(tabelaFormatada, tagsCore, connectionString);
                    }

                    using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();
                    foreach (var tag in tagsAtualizadas)
                    {
                        string insertTagQuery = @"
INSERT INTO tags_monitoramento (node_id, nome_normalizado, nome_tabela_origem, tipo_dado, display_name)
VALUES (@node_id, @nome_normalizado, @nome_tabela_origem, @tipo_dado, @display_name)
ON CONFLICT (node_id) DO UPDATE 
SET tipo_dado = EXCLUDED.tipo_dado,
    display_name = EXCLUDED.display_name;";

                        using var insertCmd = new NpgsqlCommand(insertTagQuery, conn);
                        insertCmd.Parameters.AddWithValue("@node_id", tag.NodeId);
                        insertCmd.Parameters.AddWithValue("@nome_normalizado", tag.NomeNormalizado);
                        insertCmd.Parameters.AddWithValue("@nome_tabela_origem", tabelaFormatada);
                        insertCmd.Parameters.AddWithValue("@tipo_dado", tag.TipoDado);
                        insertCmd.Parameters.AddWithValue("@display_name", (object?)tag.DisplayName ?? DBNull.Value);

                        await insertCmd.ExecuteNonQueryAsync();
                    }

                    // (Opcional) leitura e inserção dos valores reais
                    var tagValues = new Dictionary<string, object>();
                    foreach (var tag in tagsAtualizadas)
                    {
                        try
                        {
                            var rawValue = await _appManager.ReadOpcUaTag(tag.NodeId);
                            var convertedValue = DatabaseManager.ConvertTagValue(rawValue);
                            tagValues[tag.NomeNormalizado] = convertedValue;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERRO] Falha ao ler tag {tag.NodeId}: {ex.Message}");
                        }
                    }

                    await _databaseManager.SaveTagValuesAsync(tabelaFormatada, tagValues, DateTime.UtcNow, connectionString);

                    tabelasCriadas.Add(tabelaFormatada);
                }

                return Ok(tabelasCriadas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponseDB
                {
                    status = 500,
                    message = "Erro ao replicar tabelas por equipamento.",
                    error = ex.Message
                });
            }
        }






        public class CreateInsertResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string tableName { get; set; }
            public Dictionary<string, object> insertedTags { get; set; } // ← Aqui estava List<string>, agora está certo
        }




        [HttpPost("connect")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DatabaseConnectResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseDB))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseDB))]
        public async Task<IActionResult> ConnectDatabase(
    [FromQuery] string? connectionString,
    [FromBody] DatabaseConnectRequest? request)
        {
            Console.WriteLine("[DEBUG] Recebida solicitação de conexão ao banco.");

            connectionString ??= request?.ConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return BadRequest(new ErrorResponseDB
                {
                    status = 400,
                    message = "A string de conexão não pode estar vazia.",
                    exampleRequest = new { connectionString = "Host=localhost;Port=5432;Username=postgres;Password=1234;Database=meubanco" }
                });
            }

            try
            {
                bool conectado = _appManager.ConnectToDatabase(connectionString);
                Console.WriteLine($"[DEBUG] Resultado da conexão: {conectado}");

                if (!conectado)
                {
                    return BadRequest(new ErrorResponseDB
                    {
                        status = 500,
                        message = "Erro ao conectar ao banco de dados. Verifique as credenciais e a disponibilidade do banco.",
                        connectionString = connectionString
                    });
                }

                // ✅ Espera rápida para garantir que _currentConnectionString foi registrada
                await Task.Delay(100);

                // ✅ Prepara os bancos para monitoramento (agora que a string está pronta)
                //_appManager.PrepararTodosOsBancosParaMonitoramento();

                // ✅ Ativa sinal para o BackgroundService
                //_statusManager.SetBancoConectado(true);

                return Ok(new DatabaseConnectResponse
                {
                    status = 200,
                    message = "Conectado ao banco de dados com sucesso.",
                    connectionString = connectionString
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponseDB
                {
                    status = 500,
                    message = "Erro ao tentar conectar ao banco de dados.",
                    error = ex.Message,
                    connectionString = connectionString
                });
            }
        }




        /// <summary>
        /// Requisição para conectar ao banco de dados.
        /// </summary>
        public class DatabaseConnectRequest
        {
            public string ConnectionString { get; set; }
        }

        /// <summary>
        /// Resposta ao conectar ao banco de dados.
        /// </summary>
        public class DatabaseConnectResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public string connectionString { get; set; }
        }

        /// <summary>
        /// Modelo de erro padronizado para conexões ao banco de dados.
        /// </summary>
        public class ErrorResponseDB
        {
            public int status { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string connectionString { get; set; }
            public List<string> availableDatabases { get; set; } // 🔹 Lista de bancos disponíveis
            public object exampleRequest { get; set; }
        }



        [HttpGet("list")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DatabaseListResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseDB))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseDB))]
        public IActionResult ListDatabases()
        {
            Console.WriteLine("[DEBUG] Recebida solicitação para listar bancos de dados.");

            // 🔹 Obtém a conexão ativa
            string connectionString = _appManager.GetCurrentDatabaseConnectionString();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return BadRequest(new ErrorResponseDB
                {
                    status = 400,
                    message = "Nenhuma conexão ativa com o banco de dados. Conecte-se antes de listar os bancos."
                });
            }

            try
            {
                var databases = _databaseManager.ListDatabases(connectionString);

                if (databases == null || databases.Count == 0)
                {
                    return Ok(new DatabaseListResponse
                    {
                        status = 200,
                        message = "Nenhum banco de dados encontrado.",
                        databases = new List<string>()
                    });
                }

                return Ok(new DatabaseListResponse
                {
                    status = 200,
                    message = "Lista de bancos de dados obtida com sucesso.",
                    databases = databases
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao listar bancos de dados: {ex.Message}");
                return StatusCode(500, new ErrorResponseDB
                {
                    status = 500,
                    message = "Erro ao tentar listar os bancos de dados.",
                    error = ex.Message
                });
            }
        }


        /// <summary>
        /// Resposta ao listar os bancos de dados disponíveis.
        /// </summary>
        public class DatabaseListResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public List<string> databases { get; set; }
        }


        [HttpPost("select")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SelectDatabaseResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseDB))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseDB))]
        public IActionResult SelectDatabase(
    [FromQuery] string? databaseName, // 🔹 Permite envio via Query String
    [FromBody] DatabaseSelectRequest? request // 🔹 Permite envio via JSON
)
        {
            // 🔹 Se o nome do banco não veio na Query, tenta pegar do JSON
            databaseName ??= request?.DatabaseName;

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return BadRequest(new ErrorResponseDB
                {
                    status = 400,
                    message = "O nome do banco de dados não pode ser vazio.",
                    exampleRequest = new { databaseName = "meu_banco" }
                });
            }

            // 🔹 Obtém a conexão ativa do usuário
            string connectionString = _appManager.GetCurrentDatabaseConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return BadRequest(new ErrorResponseDB
                {
                    status = 400,
                    message = "Nenhuma conexão ativa com o banco de dados. Conecte-se antes de selecionar um banco."
                });
            }

            try
            {
                // 🔹 Lista os bancos disponíveis no servidor atual
                var availableDatabases = _databaseManager.ListDatabases(connectionString);

                // 🔹 Verifica se o banco desejado está na lista de bancos do usuário
                if (!availableDatabases.Contains(databaseName))
                {
                    return BadRequest(new ErrorResponseDB
                    {
                        status = 400,
                        message = $"O banco '{databaseName}' não existe ou não pertence ao servidor conectado.",
                        availableDatabases = availableDatabases // Mostra os bancos disponíveis
                    });
                }

                // 🔹 Agora seleciona o banco de dados
                bool success = _appManager.SelectDatabase(databaseName);

                if (!success)
                {
                    return StatusCode(500, new ErrorResponseDB
                    {
                        status = 500,
                        message = $"Erro ao selecionar o banco de dados '{databaseName}'."
                    });
                }

                return Ok(new SelectDatabaseResponse
                {
                    status = 200,
                    message = $"Banco de dados '{databaseName}' selecionado com sucesso."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponseDB
                {
                    status = 500,
                    message = "Erro ao tentar selecionar o banco de dados.",
                    error = ex.Message
                });
            }
        }



        public class DatabaseSelectRequest
        {
            public string DatabaseName { get; set; }
        }


        public class SelectDatabaseResponse
        {
            public int status { get; set; }
            public string message { get; set; }
        }




        [HttpGet("tables")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TablesListResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseDB))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseDB))]
        public IActionResult GetTables()
        {
            Console.WriteLine("[DEBUG] Recebida solicitação para listar tabelas do banco selecionado.");

            // 🔹 Obtém a conexão ativa do banco selecionado
            string connectionString = _appManager.GetCurrentDatabaseConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return BadRequest(new ErrorResponseDB
                {
                    status = 400,
                    message = "Nenhuma conexão ativa com o banco de dados. Conecte-se antes de listar as tabelas."
                });
            }

            try
            {
                var tables = _databaseManager.GetTables(connectionString);

                if (tables == null || tables.Count == 0)
                {
                    return Ok(new TablesListResponse
                    {
                        status = 200,
                        message = "Nenhuma tabela encontrada no banco de dados.",
                        tables = new List<string>()
                    });
                }

                return Ok(new TablesListResponse
                {
                    status = 200,
                    message = "Lista de tabelas obtida com sucesso.",
                    tables = tables
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao listar tabelas: {ex.Message}");
                return StatusCode(500, new ErrorResponseDB
                {
                    status = 500,
                    message = "Erro ao tentar listar as tabelas.",
                    error = ex.Message
                });
            }
        }


        [HttpGet("tables/{tableName}")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ErrorResponseDB))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ErrorResponseDB))]
        public IActionResult GetTableOnlyTags(string tableName)
        {
            Console.WriteLine($"[DEBUG] Solicitado conteúdo de tags da tabela: {tableName}");

            string connectionString = _appManager.GetCurrentDatabaseConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return BadRequest(new ErrorResponseDB
                {
                    status = 400,
                    message = "Nenhuma conexão ativa com o banco de dados. Conecte-se antes de buscar os dados da tabela."
                });
            }

            try
            {
                var result = _databaseManager.GetTableData(tableName, connectionString);

                var tagsSelecionadas = new Dictionary<string, object>();
                var equipamentosDict = new Dictionary<string, Dictionary<string, object>>();
                int contador = 1;

                foreach (var linha in result)
                {
                    string equipamento = linha.ContainsKey("equipamento") ? linha["equipamento"]?.ToString() ?? "" : "";

                    // Tag de exemplo (não é equipamento real)
                    if (equipamento.StartsWith("Tag selecionada"))
                    {
                        if (linha.TryGetValue("tag_d", out var displayNameObj) &&
                            linha.TryGetValue("tag_t", out var tipoObj))
                        {
                            string displayName = displayNameObj?.ToString() ?? "";
                            string tipo = tipoObj?.ToString() ?? "";

                            tagsSelecionadas[$"Tag selecionada {contador:00}"] = new
                            {
                                nome = displayName.Trim('"'),
                                tipo = tipo
                            };
                            contador++;
                        }
                        continue;
                    }

                    // Equipamento real
                    if (!equipamentosDict.ContainsKey(equipamento))
                        equipamentosDict[equipamento] = new Dictionary<string, object> { ["equipamento"] = equipamento };

                    if (linha.TryGetValue("tag_d", out var tagNomeObj))
                    {
                        string nomeTag = tagNomeObj?.ToString() ?? "";
                        var valor = linha.ContainsKey("tag_v") ? linha["tag_v"] : null;

                        equipamentosDict[equipamento][nomeTag] = valor;
                    }
                }

                var equipamentos = equipamentosDict.Values.ToList();

                return Ok(new
                {
                    status = 200,
                    message = $"Dados da tabela '{tableName}' obtidos com sucesso.",
                    tagsSelecionadas,
                    equipamentos
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao obter dados da tabela {tableName}: {ex.Message}");
                return StatusCode(500, new ErrorResponseDB
                {
                    status = 500,
                    message = "Erro ao tentar buscar os dados da tabela.",
                    error = ex.Message
                });
            }
        }


        /// <summary>
        /// Resposta ao listar as tabelas do banco de dados.
        /// </summary>
        public class TablesListResponse
        {
            public int status { get; set; }
            public string message { get; set; }
            public List<string> tables { get; set; }
        }

    }
}
