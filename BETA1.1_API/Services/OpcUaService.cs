using CoreServices.Services;
using Opc.Ua;
using System;
using System.Threading.Tasks;

namespace MyOpcUaApi.Services
{
    public class OpcUaService
    {
        private readonly OpcUaClient _opcUaClient;

        public OpcUaService(OpcUaClient opcUaClient)
        {
            _opcUaClient = opcUaClient;
        }

        public async Task ConnectAsync(string serverUrl)
        {
            await _opcUaClient.ConnectAsync(serverUrl, "OpcUaClient.Config.json");
        }

        public async Task<object> ReadTagAsync(string nodeId)
        {
            return await _opcUaClient.ReadValueAsync(new NodeId(nodeId));
        }

        public async Task WriteTagAsync(string nodeId, object value)
        {
            await _opcUaClient.WriteValueAsync(new NodeId(nodeId), value);
        }
    }
}
