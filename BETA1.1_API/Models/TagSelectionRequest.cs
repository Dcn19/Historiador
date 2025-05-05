using static MyOpcUaApi.Controllers.OpcUaController;

namespace BETA1._1_API.Models
{
    public class TagSelectionRequest
    {
        public List<string> SelectedTags { get; set; } = new();
        public string? TableName { get; set; }
    }
}
