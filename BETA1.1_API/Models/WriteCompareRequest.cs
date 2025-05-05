namespace MyOpcUaApi.Models
{
    public class WriteCompareRequest
    {
        public string NodeId { get; set; }
        public object Value { get; set; }
        public string Comparator { get; set; } // Exemplo: "=", ">", "<", ">=", "<="
    }
}
