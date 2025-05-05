namespace MyOpcUaApi.Models
{
    public class CreateAndInsertRequest
    {
        public string TableName { get; set; }
        public Dictionary<string, object> TagValues { get; set; }
    }
}

