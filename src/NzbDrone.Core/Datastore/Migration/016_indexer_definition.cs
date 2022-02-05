using System.Data;
using System.Text.Json;
using FluentMigrator;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(16)]
    public class indexer_definition : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Indexers")
                .AddColumn("DefinitionFile").AsString().Nullable();

            Execute.WithConnection(MigrateCardigannDefinitions);
        }

        private void MigrateCardigannDefinitions(IDbConnection conn, IDbTransaction tran)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = "SELECT \"Id\", \"Settings\" FROM \"Indexers\" WHERE \"Implementation\" = 'Cardigann'";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetInt32(0);
                        var settings = reader.GetString(1);
                        if (!string.IsNullOrWhiteSpace(settings))
                        {
                            var jsonObject = STJson.Deserialize<JsonElement>(settings);

                            var defFile = string.Empty;

                            if (jsonObject.TryGetProperty("definitionFile", out JsonElement jsonDef))
                            {
                                defFile = jsonDef.GetString();
                            }

                            settings = jsonObject.ToJson();
                            using (var updateCmd = conn.CreateCommand())
                            {
                                updateCmd.Transaction = tran;
                                updateCmd.CommandText = "UPDATE \"Indexers\" SET \"DefinitionFile\" = ? WHERE \"Id\" = ?";
                                updateCmd.AddParameter(defFile);
                                updateCmd.AddParameter(id);
                                updateCmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
            }
        }
    }
}
