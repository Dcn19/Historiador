namespace MyOpcUaApi.Models
{
    public class DatabaseRequest
    {
        public string TableName { get; set; }
        public Dictionary<string, object> TagValues { get; set; }
    }
}

