using CoreServices.Models;
using CoreServices.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyOpcUaApi.Services
{
    public class DatabaseService
    {
        private readonly DatabaseManager _databaseManager;

        public DatabaseService(DatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
        }

        public void CreateTable(string tableName, List<TagInfo> tags, string connectionString)
        {
            _databaseManager.CreateTable(tableName, tags, connectionString);
        }


        public void InsertValues(string tableName, Dictionary<string, object> tagValues)
        {
            _databaseManager.SaveTagValues(tableName, tagValues);
        }
    }
}
