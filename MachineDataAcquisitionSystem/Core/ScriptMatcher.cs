using System;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core
{
    public static class ScriptMatcher
    {
        public static ParseScript Match(int machineId, string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();

            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();

                string sql = @"
                    SELECT s.Id, s.Name, s.ModelId, s.FileExtension, s.ScriptCode, s.IsEnabled
                    FROM ParseScripts s
                    INNER JOIN ScriptMachines sm ON s.Id = sm.ScriptId
                    WHERE s.IsEnabled = 1 
                      AND s.FileExtension = @Extension
                      AND sm.MachineId = @MachineId
                    LIMIT 1";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Extension", extension);
                    cmd.Parameters.AddWithValue("@MachineId", machineId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new ParseScript
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                ModelId = reader.GetInt32(2),
                                FileExtension = reader.GetString(3),
                                ScriptCode = reader.GetString(4),
                                IsEnabled = reader.GetInt32(5) == 1
                            };
                        }
                    }
                }
            }

            return null;
        }
    }
}