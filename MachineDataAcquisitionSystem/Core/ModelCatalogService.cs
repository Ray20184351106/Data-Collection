using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace MachineDataAcquisitionSystem.Core
{
    /// <summary>
    /// Reads the active data-model catalog directly from SQLite for model-dependent editors.
    /// The service intentionally keeps no in-memory cache so a model saved in another editor
    /// is visible on the next refresh.
    /// </summary>
    public sealed class ModelCatalogService
    {
        private readonly string _connectionString;

        public ModelCatalogService(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A database connection string is required.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public IReadOnlyList<ModelCatalogItem> LoadActiveModels()
        {
            var models = new List<ModelCatalogItem>();
            using (var connection = new SQLiteConnection(_connectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT Id, ModelName, TableName
FROM DataModels
WHERE IsActive = @IsActive
ORDER BY ModelName COLLATE BINARY, Id;";
                command.Parameters.AddWithValue("@IsActive", 1);

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        models.Add(new ModelCatalogItem(
                            reader.GetInt32(0),
                            reader.GetString(1),
                            reader.GetString(2)));
                    }
                }
            }

            return models;
        }
    }

    public sealed class ModelCatalogItem
    {
        public ModelCatalogItem(int id, string modelName, string tableName)
        {
            Id = id;
            ModelName = modelName;
            TableName = tableName;
        }

        public int Id { get; }
        public string ModelName { get; }
        public string TableName { get; }
    }
}
