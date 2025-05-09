// Novo serviço para orquestrar a criação e inserção
using CoreServices.Services;
using CoreServices;
using Microsoft.Extensions.Caching.Memory;
using MyOpcUaApi.Models;
using Npgsql;
using static MyOpcUaApi.Controllers.DatabaseController;
using CoreServices.Models;

public class DatabaseOrchestratorService
{
    private readonly ApplicationManager _appManager;
    private readonly DatabaseManager _databaseManager;
    private readonly IMemoryCache _memoryCache;

    public DatabaseOrchestratorService(ApplicationManager appManager, DatabaseManager databaseManager, IMemoryCache memoryCache)
    {
        _appManager = appManager;
        _databaseManager = databaseManager;
        _memoryCache = memoryCache;
    }

    public async Task<CreateInsertResponse> ExecuteCreateAndInsertAsync()
    {
        if (!_memoryCache.TryGetValue("selected_tags", out List<TagInfoRequest>? selectedTags) || selectedTags == null || !selectedTags.Any())
            throw new InvalidOperationException("Nenhuma tag selecionada encontrada ou a seleção expirou.");

        if (!_memoryCache.TryGetValue("selected_tableName", out string? tableName) || string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("Nome da tabela não encontrado no cache.");

        string connectionString = _appManager.GetCurrentDatabaseConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Nenhuma conexão ativa com o banco de dados.");

        _databaseManager.EnsureTagsMonitoramentoTableExists(connectionString);
        string formattedTableName = _databaseManager.NormalizeTagName(tableName);

        var tags = selectedTags.Select(tag => new MyOpcUaApi.Models.TagInfoRequest(
            id: 0,
            nodeId: tag.NodeId,
            nomeNormalizado: _databaseManager.NormalizeTagName(tag.NodeId),
            tipoDado: tag.TipoDado,
            nomeTabelaOrigem: formattedTableName,
            displayName: _appManager.GetOpcUaNodeDisplayName(tag.NodeId)
        )).ToList();

        var tagsCore = tags.Select(tag => new CoreServices.Models.TagInfo(
            id: tag.Id,
            nodeIdString: tag.NodeId,
            tagName: tag.NomeNormalizado,
            postgreType: _databaseManager.MapOpcUaDataTypeToPostgreSql(tag.TipoDado ?? "text"),
            nomeTabelaOrigem: tag.NomeTabelaOrigem
        )).ToList();

        if (!_databaseManager.TableExists(formattedTableName, connectionString))
            _databaseManager.CreateTable(formattedTableName, tagsCore, connectionString);

        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        foreach (var tag in tags)
        {
            string insertTagQuery = @"INSERT INTO tags_monitoramento (node_id, nome_normalizado, nome_tabela_origem, tipo_dado, display_name)
VALUES (@node_id, @nome_normalizado, @nome_tabela_origem, @tipo_dado, @display_name)
ON CONFLICT (node_id) DO UPDATE SET tipo_dado = EXCLUDED.tipo_dado, display_name = EXCLUDED.display_name;";

            using var insertCmd = new NpgsqlCommand(insertTagQuery, conn);
            insertCmd.Parameters.AddWithValue("@node_id", tag.NodeId);
            insertCmd.Parameters.AddWithValue("@nome_normalizado", tag.NomeNormalizado);
            insertCmd.Parameters.AddWithValue("@nome_tabela_origem", formattedTableName);
            insertCmd.Parameters.AddWithValue("@tipo_dado", tag.TipoDado ?? "text");
            insertCmd.Parameters.AddWithValue("@display_name", (object?)tag.DisplayName ?? DBNull.Value);

            await insertCmd.ExecuteNonQueryAsync();
        }

        var tagValues = new Dictionary<string, object>();
        foreach (var tag in tags)
        {
            try
            {
                var rawValue = await _appManager.ReadOpcUaTag(tag.NodeId);
                tagValues[tag.NomeNormalizado] = DatabaseManager.ConvertTagValue(rawValue);
            }
            catch { }
        }

        await _databaseManager.SaveTagValuesAsync(formattedTableName, tagValues, DateTime.UtcNow, connectionString);

        _memoryCache.Remove("selected_tags");
        _memoryCache.Remove("selected_tableName");
        _memoryCache.Remove("current_nodeId");

        return new CreateInsertResponse
        {
            status = 200,
            message = $"Tabela '{formattedTableName}' criada (se necessário) e valores reais inseridos com sucesso.",
            tableName = formattedTableName,
            insertedTags = tagValues
        };
    }

    public async Task<CreateInsertResponse> ExecuteCreateTableOnlyAsync()
    {
        if (!_memoryCache.TryGetValue("selected_tags", out List<TagInfoRequest>? selectedTags) || selectedTags == null || !selectedTags.Any())
            throw new InvalidOperationException("Nenhuma tag selecionada encontrada ou a seleção expirou.");

        if (!_memoryCache.TryGetValue("selected_tableName", out string? tableName) || string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("Nome da tabela não encontrado no cache.");

        string connectionString = _appManager.GetCurrentDatabaseConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Nenhuma conexão ativa com o banco de dados.");

        string formattedTableName = _databaseManager.NormalizeTagName(tableName);

        var columns = new List<CoreServices.Models.TagInfo>();

        foreach (var tag in selectedTags)
        {
            string nodeId = tag.NodeId;

            if (nodeId.EndsWith("_v") || nodeId.EndsWith("_d") || nodeId.EndsWith("_t"))
                continue;

            string escaped = nodeId.Replace("\"", "\"\"");

            columns.Add(new(0, escaped, escaped, "text", formattedTableName));                        // tag
            columns.Add(new(0, $"{escaped}_v", $"{escaped}_v", "text", formattedTableName));          // valor
            columns.Add(new(0, $"{escaped}_d", $"{escaped}_d", "text", formattedTableName));          // displayname
            columns.Add(new(0, $"{escaped}_t", $"{escaped}_t", "text", formattedTableName));          // tipo
        }

        if (!_databaseManager.TableExists(formattedTableName, connectionString))
            _databaseManager.CreateTable(formattedTableName, columns, connectionString);

        _memoryCache.Remove("selected_tags");
        _memoryCache.Remove("selected_tableName");

        return new CreateInsertResponse
        {
            status = 200,
            message = $"Tabela '{formattedTableName}' criada com sucesso.",
            tableName = formattedTableName
        };
    }



    public async Task<CreateInsertResponse> ExecuteCreateTableAndInsertRowsAsync()
    {
        if (!_memoryCache.TryGetValue("selected_tags", out List<TagInfoRequest>? selectedTags) || selectedTags == null || !selectedTags.Any())
            throw new InvalidOperationException("Nenhuma tag selecionada encontrada ou a seleção expirou.");

        if (!_memoryCache.TryGetValue("selected_tableName", out string? tableName) || string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("Nome da tabela não encontrado no cache.");

        string connectionString = _appManager.GetCurrentDatabaseConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Nenhuma conexão ativa com o banco de dados.");

        string formattedTableName = _databaseManager.NormalizeTagName(tableName);

        // Define as colunas fixas da tabela
        var columns = new List<TagInfo>
    {
        new(0, "equipamento", "equipamento", "TEXT", formattedTableName),
        new(0, "tag", "tag", "TEXT", formattedTableName),
        new(0, "tag_v", "tag_v", "TEXT", formattedTableName),
        new(0, "tag_d", "tag_d", "TEXT", formattedTableName),
        new(0, "tag_t", "tag_t", "TEXT", formattedTableName),
    };

        if (!_databaseManager.TableExists(formattedTableName, connectionString))
            _databaseManager.CreateTable(formattedTableName, columns, connectionString);

        int contador = 1;
        foreach (var tag in selectedTags)
        {
            try
            {
                var valorLido = await _appManager.ReadOpcUaTag(tag.NodeId);
                var valorConvertido = DatabaseManager.ConvertTagValue(valorLido);
                var displayName = _appManager.GetOpcUaNodeDisplayName(tag.NodeId);
                var tipo = valorLido?.GetType().Name ?? "null";

                var valores = new Dictionary<string, object>
            {
                { "equipamento", $"Tag selecionada {contador:00}" },
                { "tag", tag.NodeId },
                { "tag_v", valorConvertido },
                { "tag_d", displayName },
                { "tag_t", tipo },
            };

                await _databaseManager.InsertRowAsync(formattedTableName, valores, connectionString);
                contador++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao ler/inserir a tag {tag.NodeId}: {ex.Message}");
            }
        }

        _memoryCache.Remove("selected_tags");
        _memoryCache.Remove("selected_tableName");

        return new CreateInsertResponse
        {
            status = 200,
            message = $"Tabela '{formattedTableName}' criada com sucesso e tags de exemplo inseridas.",
            tableName = formattedTableName
        };
    }
}