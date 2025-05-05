namespace MyOpcUaApi.Models
{
    public class TagInfoRequest
    {
        public int Id { get; set; }
        public string NodeId { get; set; }
        public string NomeNormalizado { get; set; }
        public string TipoDado { get; set; }
        public string NomeTabelaOrigem { get; set; }
        public string? DisplayName { get; set; }  // << Adicionado!

        public TagInfoRequest(int id, string nodeId, string nomeNormalizado, string tipoDado, string nomeTabelaOrigem, string? displayName = null)
        {
            Id = id;
            NodeId = nodeId;
            NomeNormalizado = nomeNormalizado;
            TipoDado = tipoDado;
            NomeTabelaOrigem = nomeTabelaOrigem;
            DisplayName = displayName;
        }
    }
}
