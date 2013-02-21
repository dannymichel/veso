﻿using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Sqlite
{
    /// <summary>
    /// Class SQLiteDisplayPreferencesRepository
    /// </summary>
    [Export(typeof(IDisplayPreferencesRepository))]
    class SQLiteDisplayPreferencesRepository : SqliteRepository, IDisplayPreferencesRepository
    {
        /// <summary>
        /// The repository name
        /// </summary>
        public const string RepositoryName = "SQLite";

        /// <summary>
        /// Gets the name of the repository
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get
            {
                return RepositoryName;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteUserDataRepository" /> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        [ImportingConstructor]
        protected SQLiteDisplayPreferencesRepository([Import("logger")] ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        /// Opens the connection to the database
        /// </summary>
        /// <returns>Task.</returns>
        public async Task Initialize()
        {
            var dbFile = Path.Combine(Kernel.Instance.ApplicationPaths.DataPath, "displaypreferences.db");

            await ConnectToDB(dbFile).ConfigureAwait(false);

            string[] queries = {

                                "create table if not exists display_prefs (item_id GUID, user_id GUID, data BLOB)",
                                "create unique index if not exists idx_display_prefs on display_prefs (item_id, user_id)",
                                "create table if not exists schema_version (table_name primary key, version)",
                                //pragmas
                                "pragma temp_store = memory"
                               };

            RunQueries(queries);
        }

        /// <summary>
        /// Save the display preferences associated with an item in the repo
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException">item</exception>
        public Task SaveDisplayPrefs(Folder item, CancellationToken cancellationToken)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            if (cancellationToken == null)
            {
                throw new ArgumentNullException("cancellationToken");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return Task.Run(() =>
            {
                var cmd = connection.CreateCommand();

                cmd.CommandText = "delete from display_prefs where item_id = @guid";
                cmd.AddParam("@guid", item.DisplayPrefsId);

                QueueCommand(cmd);

                if (item.DisplayPrefs != null)
                {
                    foreach (var data in item.DisplayPrefs)
                    {
                        cmd = connection.CreateCommand();
                        cmd.CommandText = "insert into display_prefs (item_id, user_id, data) values (@1, @2, @3)";
                        cmd.AddParam("@1", item.DisplayPrefsId);
                        cmd.AddParam("@2", data.UserId);

                        cmd.AddParam("@3", Kernel.Instance.ProtobufSerializer.SerializeToBytes(data));

                        QueueCommand(cmd);
                    }
                }
            });
        }

        /// <summary>
        /// Gets display preferences for an item
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>IEnumerable{DisplayPreferences}.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public IEnumerable<DisplayPreferences> RetrieveDisplayPrefs(Folder item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            var cmd = connection.CreateCommand();
            cmd.CommandText = "select data from display_prefs where item_id = @guid";
            var guidParam = cmd.Parameters.Add("@guid", DbType.Guid);
            guidParam.Value = item.DisplayPrefsId;

            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult))
            {
                while (reader.Read())
                {
                    using (var stream = GetStream(reader, 0))
                    {
                        var data = Kernel.Instance.ProtobufSerializer.DeserializeFromStream<DisplayPreferences>(stream);
                        if (data != null)
                        {
                            yield return data;
                        }
                    }
                }
            }
        }
    }
}
