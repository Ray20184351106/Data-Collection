using System;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ModelCatalogServiceTests
    {
        [Fact]
        public void ReloadActiveModels_includes_a_model_saved_after_the_previous_load()
        {
            string databasePath = Path.Combine(Path.GetTempPath(), "model-catalog-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                CreateModelTable(databasePath);
                InsertModel(databasePath, 1, "ExistingModel", 1);
                var service = new ModelCatalogService("Data Source=" + databasePath + ";Version=3;");

                Assert.Single(service.LoadActiveModels());

                InsertModel(databasePath, 2, "NewlySavedModel", 1);
                var refreshed = service.LoadActiveModels();

                Assert.Collection(
                    refreshed,
                    first => Assert.Equal("ExistingModel", first.ModelName),
                    second => Assert.Equal("NewlySavedModel", second.ModelName));
            }
            finally
            {
                if (File.Exists(databasePath))
                    File.Delete(databasePath);
            }
        }

        private static void CreateModelTable(string databasePath)
        {
            using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
CREATE TABLE DataModels (
    Id INTEGER PRIMARY KEY,
    ModelName TEXT NOT NULL,
    TableName TEXT NOT NULL,
    IsActive INTEGER NOT NULL);";
                command.ExecuteNonQuery();
            }
        }

        private static void InsertModel(string databasePath, int id, string modelName, int isActive)
        {
            using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
INSERT INTO DataModels (Id, ModelName, TableName, IsActive)
VALUES (@Id, @ModelName, @TableName, @IsActive);";
                command.Parameters.AddWithValue("@Id", id);
                command.Parameters.AddWithValue("@ModelName", modelName);
                command.Parameters.AddWithValue("@TableName", modelName);
                command.Parameters.AddWithValue("@IsActive", isActive);
                command.ExecuteNonQuery();
            }
        }
    }
}
