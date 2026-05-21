    using FirebirdSql.Data.FirebirdClient;
    using Microsoft.Extensions.Logging;
    using System.Data;

    namespace CustomController;

    public class DB : IDisposable
    {
        private readonly string _connectionString;
        private bool _disposed = false;

        public DB(string connectionString)
        {
            _connectionString = connectionString;
        }

        private FbConnection CreateConnection() => new FbConnection(_connectionString);

        public bool CheckRequiredTables(ILogger logger)
        {
            logger.LogInformation("Проверка подключения к базе данных...");

            using var con = CreateConnection();
            try
            {
                con.Open();
                logger.LogInformation("Подключение к БД успешно.");

                string[] requiredTables = { "DEVICE", "BAS_PARAM", "CARDINDEV", "CARD", "CARDIDX" };

                var missing = new List<string>();
                foreach (var table in requiredTables)
                {
                    if (!TableExists(table, con))
                        missing.Add(table);
                }

                if (missing.Any())
                {
                    logger.LogCritical($"Отсутствуют таблицы: {string.Join(", ", missing)}");
                    return false;
                }

                logger.LogInformation("Все обязательные таблицы найдены.");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка подключения к БД");
                return false;
            }
        }

        private bool TableExists(string tableName, FbConnection connection)
        {
            string sql = @"SELECT COUNT(*) FROM RDB$RELATIONS 
                           WHERE RDB$RELATION_NAME = @table AND RDB$SYSTEM_FLAG = 0";

            using var cmd = new FbCommand(sql, connection);
            cmd.Parameters.AddWithValue("@table", tableName.ToUpper());
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public DataTable GetDevice()
        {
            using var con = CreateConnection();
            con.Open();

            string sql = @"
                SELECT 
                    d.id_dev,
                    bp.intvalue as IP,
                    d.id_ctrl as ctrl,
                    SUBSTRING(d.NAME FROM 1 FOR 31) as CTRL_NAME
                FROM device d
                LEFT JOIN bas_param bp ON d.id_dev = bp.id_dev AND bp.param = 'IP'
                WHERE bp.intvalue IS NOT NULL";

            using var cmd = new FbCommand(sql, con);
            var dt = new DataTable();
            dt.Load(cmd.ExecuteReader());
            return dt;
        }

        public DataTable GetCardForLoad(int id_dev)
        {
            using var con = CreateConnection();
            con.Open();

            // Более строгий запрос — привязываем строго к id_dev
            string sql = @"
            SELECT cd.id_cardindev, cd.id_card, cd.id_dev, cd.operation 
            FROM cardindev cd
            WHERE cd.id_dev = @id_dev";

            using var cmd = new FbCommand(sql, con);
            cmd.Parameters.AddWithValue("@id_dev", id_dev);

            var dt = new DataTable();
            dt.Load(cmd.ExecuteReader());
            return dt;
        }

    public void DeleteCardInDev(int id_cardindev)
        {
            using var con = CreateConnection();
            con.Open();
            using var cmd = new FbCommand("DELETE FROM cardindev WHERE id_cardindev = @id", con);
            cmd.Parameters.AddWithValue("@id", id_cardindev);
            cmd.ExecuteNonQuery();
        }

        public void UpdateCardInDevIncrement(int id_cardindev)
        {
            using var con = CreateConnection();
            con.Open();
            using var cmd = new FbCommand("UPDATE cardindev SET attempts = attempts + 1 WHERE id_cardindev = @id", con);
            cmd.Parameters.AddWithValue("@id", id_cardindev);
            cmd.ExecuteNonQuery();
        }

        public int FixCardIdxOK(string idCard, int idDev, int uid)
        {
            using var con = CreateConnection();
            con.Open();

            // Попытка обновления
            string updateSql = @"
                UPDATE CARDIDX 
                SET DEVIDX = @uid, 
                    LOAD_TIME = CURRENT_TIMESTAMP, 
                    LOAD_RESULT = 'OK'
                WHERE ID_CARD = @idCard AND ID_DEV = @idDev";

            using (var cmd = new FbCommand(updateSql, con))
            {
                cmd.Parameters.AddWithValue("@uid", uid);
                cmd.Parameters.AddWithValue("@idCard", idCard);
                cmd.Parameters.AddWithValue("@idDev", idDev);

                int rows = cmd.ExecuteNonQuery();
                if (rows > 0) return rows;
            }

            // Вставка новой записи
            string insertSql = @"
                INSERT INTO CARDIDX (ID_CARD, ID_DEV, DEVIDX, LOAD_TIME, LOAD_RESULT)
                VALUES (@idCard, @idDev, @uid, CURRENT_TIMESTAMP, 'OK')";

            using var cmdInsert = new FbCommand(insertSql, con);
            cmdInsert.Parameters.AddWithValue("@idCard", idCard);
            cmdInsert.Parameters.AddWithValue("@idDev", idDev);
            cmdInsert.Parameters.AddWithValue("@uid", uid);

            return cmdInsert.ExecuteNonQuery();
        }

        // ==================== IDisposable ====================
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Здесь можно освободить управляемые ресурсы, если они появятся
                }
                _disposed = true;
            }
        }

        ~DB()
        {
            Dispose(false);
        }
    }