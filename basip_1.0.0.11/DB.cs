
using FirebirdSql.Data.FirebirdClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Basip
{
    class DB
    {
        private FbConnection con;

        public FbConnection DBconnect(String connect)
        {
            return con = new FbConnection(connect);
        }

        // Метод для проверки существования таблицы в базе данных
        public bool TableExists(string tableName)
        {
            try
            {
                string sql = $@"SELECT COUNT(*) 
                               FROM RDB$RELATIONS 
                               WHERE RDB$RELATION_NAME = '{tableName.ToUpper()}' 
                               AND RDB$SYSTEM_FLAG = 0";

                FbCommand command = new FbCommand(sql, con);
                var result = command.ExecuteScalar();

                return Convert.ToInt32(result) > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке таблицы {tableName}: {ex.Message}");
                return false;
            }
        }
        public void CreateCardIdxTableIfNotExists()
        {
            try
            {
                string checkTableSql = @"SELECT COUNT(*) 
                               FROM RDB$RELATIONS 
                               WHERE RDB$RELATION_NAME = 'CARDIDX' 
                               AND RDB$SYSTEM_FLAG = 0";

                FbCommand checkCommand = new FbCommand(checkTableSql, con);
                var result = checkCommand.ExecuteScalar();

                if (Convert.ToInt32(result) == 0)
                {
                    string createTableSql = @"CREATE TABLE CARDIDX (
                ID_CARDIDX INTEGER NOT NULL,
                ID_CARD VARCHAR(20) NOT NULL,
                ID_DEV INTEGER NOT NULL,
                DEVIDX INTEGER,
                LOAD_TIME TIMESTAMP,
                LOAD_RESULT VARCHAR(500),
                PRIMARY KEY (ID_CARDIDX)
            )";

                    FbCommand createCommand = new FbCommand(createTableSql, con);
                    createCommand.ExecuteNonQuery();

                    // Создание последовательности для автоинкремента
                    string createGeneratorSql = @"CREATE GENERATOR GEN_CARDIDX_ID";
                    FbCommand genCommand = new FbCommand(createGeneratorSql, con);
                    genCommand.ExecuteNonQuery();

                    Console.WriteLine("Table CARDIDX created successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating CARDIDX table: {ex.Message}");
            }
        }
        // Проверка всех обязательных таблиц
        public bool CheckRequiredTables()
        {
            string[] requiredTables = {
                "DEVICE",
                "BAS_PARAM",
                "CARDINDEV",
                "CARD",
                "CARDIDX"
            };

            var missingTables = new List<string>();

            foreach (var table in requiredTables)
            {
                if (!TableExists(table))
                {
                    missingTables.Add(table);
                }
            }

            if (missingTables.Any())
            {
                Console.WriteLine($"Отсутствующие таблицы: {string.Join(", ", missingTables)}");
                return false;
            }

            return true;
        }

        public bool EventInsert(int id_db = 1, int? id_eventtype = null, int? id_cntrl = null,
                               int? id_reader = null, string note = null, DateTime? time = null,
                               int? id_video = null, int? id_user = null, int? ess1 = null,
                               int? ess2 = null, int? idsource = null, long? idserverts = null)
        {
            try
            {
                string sql = @"EXECUTE PROCEDURE DEVICEEVENTS_INSERT(
            @id_db, @id_eventtype, @id_cntrl, @id_reader, @note, @time, 
            @id_video, @id_user, @ess1, @ess2, @idsource, @idserverts)";

                FbCommand command = new FbCommand(sql, con);

                command.Parameters.AddWithValue("@id_db", id_db);
                command.Parameters.AddWithValue("@id_eventtype", id_eventtype ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@id_cntrl", id_cntrl ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@id_reader", id_reader ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@note", note ?? (object)DBNull.Value);

                // Преобразуем DateTime в строку для базы данных
                string timeString = time?.ToString("yyyy-MM-dd HH:mm:ss") ?? "NOW";
                command.Parameters.AddWithValue("@time", timeString);

                command.Parameters.AddWithValue("@id_video", id_video ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@id_user", id_user ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@ess1", ess1 ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@ess2", ess2 ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@idsource", idsource ?? 1);
                command.Parameters.AddWithValue("@idserverts", idserverts != null ? (long)(idserverts / 1000) : (object)DBNull.Value);

                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting event: {ex.Message}");
                return false;
            }
        }
        public void DeleteDuplicateEvents(int id_cntrl, string note, DateTime eventTime)
        {
            try
            {
                // Удаляем события за последние 5 минут с таким же текстом
                string sql = @"DELETE FROM EVENTS 
                          WHERE ID_CNTRL = @id_cntrl 
                          AND READDATA = @note 
                          AND TIME_STAMP BETWEEN @startTime AND @endTime";

                FbCommand command = new FbCommand(sql, con);
                command.Parameters.AddWithValue("@id_cntrl", id_cntrl);
                command.Parameters.AddWithValue("@note", note);
                command.Parameters.AddWithValue("@startTime", eventTime.AddMinutes(-5));
                command.Parameters.AddWithValue("@endTime", eventTime.AddMinutes(5));

                int rowsDeleted = command.ExecuteNonQuery();
                if (rowsDeleted > 0)
                {
                    Console.WriteLine($"Deleted {rowsDeleted} duplicate events");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting duplicates: {ex.Message}");
            }
        }

        public DataTable GetDevice()
        {
            string sql = @"SELECT 
                d.id_dev, 
                bp.intvalue as IP,
                bp_login.strvalue as LOGIN,
                bp_pass.strvalue as PASS
            FROM device d
            LEFT JOIN bas_param bp ON d.id_dev = bp.id_dev AND bp.param = 'IP'
            LEFT JOIN bas_param bp_login ON bp_login.id_dev = d.id_dev AND bp_login.param = 'LOGIN'
            LEFT JOIN bas_param bp_pass ON bp_pass.id_dev = d.id_dev AND bp_pass.param = 'PASS'
            WHERE bp.intvalue IS NOT NULL";

            FbCommand getcomand = new FbCommand(sql, con);
            var reader = getcomand.ExecuteReader();
            DataTable table = new DataTable();
            table.Load(reader);
            return table;
        }

        public DataTable GetCardForLoad(int id_dev)
        {
            string sql = $@"select cd.id_cardindev, cd.id_card, cd.id_dev,cd.operation from cardindev cd
            join device d on d.id_dev=cd.id_dev
            join device d2 on d2.id_ctrl=d.id_ctrl and d2.id_reader is null
            where d2.id_dev={id_dev}";

            FbCommand getcomand = new FbCommand(sql, con);
            var reader = getcomand.ExecuteReader();
            DataTable table = new DataTable();
            table.Load(reader);
            return table;
        }

        public void saveParam(int id_dev, string param_name, int? data_int, string data_string)
        {
            string sql = $@"delete from bas_param bp where bp.id_dev={id_dev} and bp.param='{param_name}'";
            FbCommand getcomand = new FbCommand(sql, con);
            getcomand.ExecuteNonQuery();
            string data_int_ = (data_int == null) ? "NULL" : data_int.ToString();
            sql = $@"INSERT INTO BAS_PARAM (ID_DEV, PARAM, INTVALUE, STRVALUE) VALUES ({id_dev},'{param_name}',{data_int_},'{data_string}')";
            getcomand = new FbCommand(sql, con);
            getcomand.ExecuteNonQuery();
        }

        public void DeleteCardInDev(int id_cardindev)
        {
            FbCommand getcomand = new FbCommand($@"delete from cardindev cd
            where cd.id_cardindev ={id_cardindev}", con);
            var reader = getcomand.ExecuteReader();
            DataTable table = new DataTable();
            table.Load(reader);
        }

        public void UpdateCardInDevIncrement(int id_cardindev)
        {
            FbCommand getcomand = new FbCommand($@"update cardindev cd
            set cd.attempts=cd.attempts+1
            where cd.id_cardindev={id_cardindev}", con);
            var reader = getcomand.ExecuteReader();
            DataTable table = new DataTable();
            table.Load(reader);
        }
        public void FixCardIdxError(string idCard, int idDev, string errorMessage, FbConnection con)
        {
            try
            {
                // Сначала пытаемся обновить существующую запись
                string updateSql = @"UPDATE CARDIDX SET
            LOAD_TIME = CURRENT_TIMESTAMP,
            LOAD_RESULT = @errorMessage
            WHERE (ID_CARD = @idCard) AND (ID_DEV = @idDev)";

                FbCommand updateCommand = new FbCommand(updateSql, con);
                updateCommand.Parameters.AddWithValue("@errorMessage", errorMessage);
                updateCommand.Parameters.AddWithValue("@idCard", idCard);
                updateCommand.Parameters.AddWithValue("@idDev", idDev);

                int rowsUpdated = updateCommand.ExecuteNonQuery();

                // Если не нашли запись для обновления - вставляем новую
                if (rowsUpdated == 0)
                {
                    string insertSql = @"INSERT INTO CARDIDX 
                (ID_CARD, ID_DEV, LOAD_TIME, LOAD_RESULT) 
                VALUES (@idCard, @idDev, CURRENT_TIMESTAMP, @errorMessage)";

                    FbCommand insertCommand = new FbCommand(insertSql, con);
                    insertCommand.Parameters.AddWithValue("@idCard", idCard);
                    insertCommand.Parameters.AddWithValue("@idDev", idDev);
                    insertCommand.Parameters.AddWithValue("@errorMessage", errorMessage);

                    insertCommand.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in FixCardIdxError: {ex.Message}");
            }
        }
        public int FixCardIdxOK(string idCard, int idDev, int uid, FbConnection con)
        {
            int retryCount = 0;
            const int maxRetries = 3;

            while (retryCount < maxRetries)
            {
                try
                {
                    FbTransaction transaction = null;
                    try
                    {
                        // Начинаем транзакцию
                        transaction = con.BeginTransaction();

                        Console.WriteLine($"Attempting to fix CardIdx: ID_CARD={idCard}, ID_DEV={idDev}, UID={uid}");

                        // Сначала пытаемся обновить существующую запись
                        string updateSql = @"UPDATE CARDIDX SET
                    DEVIDX = @uid,
                    LOAD_TIME = CURRENT_TIMESTAMP,
                    LOAD_RESULT = 'OK'
                    WHERE (ID_CARD = @idCard) AND (ID_DEV = @idDev)";

                        FbCommand updateCommand = new FbCommand(updateSql, con, transaction);
                        updateCommand.Parameters.AddWithValue("@uid", uid);
                        updateCommand.Parameters.AddWithValue("@idCard", idCard);
                        updateCommand.Parameters.AddWithValue("@idDev", idDev);

                        int rowsUpdated = updateCommand.ExecuteNonQuery();
                        Console.WriteLine($"Rows updated: {rowsUpdated}");

                        // Если не нашли запись для обновления - вставляем новую
                        if (rowsUpdated == 0)
                        {
                            string insertSql = @"INSERT INTO CARDIDX 
                        (ID_CARD, ID_DEV, DEVIDX, LOAD_TIME, LOAD_RESULT) 
                        VALUES (@idCard, @idDev, @uid, CURRENT_TIMESTAMP, 'OK')";

                            FbCommand insertCommand = new FbCommand(insertSql, con, transaction);
                            insertCommand.Parameters.AddWithValue("@uid", uid);
                            insertCommand.Parameters.AddWithValue("@idCard", idCard);
                            insertCommand.Parameters.AddWithValue("@idDev", idDev);

                            rowsUpdated = insertCommand.ExecuteNonQuery();
                            Console.WriteLine($"Rows inserted: {rowsUpdated}");
                        }

                        // Коммитим транзакцию
                        transaction.Commit();
                        return rowsUpdated;
                    }
                    catch
                    {
                        // Откатываем транзакцию при ошибке
                        transaction?.Rollback();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Console.WriteLine($"Error in FixCardIdxOK (attempt {retryCount}): {ex.Message}");

                    if (retryCount >= maxRetries)
                    {
                        Console.WriteLine($"Max retries reached for card {idCard}");
                        return 0;
                    }

                    // Ждем перед повторной попыткой
                    System.Threading.Thread.Sleep(100 * retryCount);
                }
            }

            return 0;
        }
    }
}
        /* 12.03.2025 для всех карт для указанной панели добавить load_result как ошибка.
         * @input id_dev - id панели
         * @input messErr - сообщение, которое надо вписать в load_result
         * 
         */
        /*
        public void updateCaridxErrAll(int id_dev, string messErr) {

            string sql = $@"delete from bas_param bp where bp.id_dev={id_dev} and bp.param='{param_name}'";
            FbCommand getcomand = new FbCommand(sql, con);
            getcomand.ExecuteNonQuery();
            string data_int_ = (data_int == null) ? "NULL" : data_int.ToString();
            sql = $@"INSERT INTO BAS_PARAM (ID_DEV, PARAM, INTVALUE, STRVALUE) VALUES ({id_dev},'{param_name}',{data_int_},'{data_string}')";
            getcomand = new FbCommand(sql, con);
            getcomand.ExecuteNonQuery();
        }*/