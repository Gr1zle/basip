using FirebirdSql.Data.FirebirdClient;
using System.Data;

public class DB
{
    private string _connectionString;

    public DB(string connectionString)
    {
        _connectionString = connectionString;
    }

    private FbConnection CreateConnection()
    {
        return new FbConnection(_connectionString);
    }

    // Метод для проверки существования таблицы в базе данных
    public bool TableExists(string tableName)
    {
        using var con = CreateConnection();
        try
        {
            con.Open();
            string sql = $@"SELECT COUNT(*) 
                           FROM RDB$RELATIONS 
                           WHERE RDB$RELATION_NAME = '{tableName.ToUpper()}' 
                           AND RDB$SYSTEM_FLAG = 0";

            using var command = new FbCommand(sql, con);
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при проверке таблицы {tableName}: {ex.Message}");
            return false;
        }
    }

    // Проверка всех обязательных таблиц
    public bool CheckRequiredTables(ILogger logger)
    {
        logger.LogInformation($"Попытка подключения к БД. Строка подключения: {_connectionString}");

        using var con = CreateConnection();
        try
        {
            con.Open();
            logger.LogInformation("Подключение к базе данных успешно установлено.");

            string[] requiredTables = {
            "DEVICE",
            "BAS_PARAM",
            "CARDINDEV",
            "CARD",
            "CARDIDX"
        };

            logger.LogInformation($"Начинаю проверку обязательных таблиц: {string.Join(", ", requiredTables)}");

            var missingTables = new List<string>();

            foreach (var table in requiredTables)
            {
                bool exists = TableExists(table);
                logger.LogDebug($"Таблица '{table}': {(exists ? "НАЙДЕНА" : "ОТСУТСТВУЕТ")}"); // 3. Логируем результат для каждой таблицы
                if (!exists)
                {
                    missingTables.Add(table);
                }
            }

            if (missingTables.Any())
            {
                // 4. Используем logger вместо Console.WriteLine
                logger.LogCritical($"КРИТИЧЕСКАЯ ОШИБКА: В базе данных отсутствуют обязательные таблицы: {string.Join(", ", missingTables)}");
                return false;
            }

            logger.LogInformation("Проверка обязательных таблиц завершена успешно. Все таблицы найдены.");
            return true;
        }
        catch (Exception ex)
        {
            // 5. Логируем саму ошибку подключения
            logger.LogError($"ОШИБКА ПОДКЛЮЧЕНИЯ К БАЗЕ ДАННЫХ: {ex.Message}");
            return false;
        }
    }

    public bool SetLastEventID(int id_dev, long eventId)
    {
        using var con = CreateConnection();
        try
        {
            con.Open();

            string deleteSql = @"delete from bas_param bp 
                                where bp.id_dev = @id_dev 
                                and bp.param = 'LASTEVENT'";

            using var deleteCommand = new FbCommand(deleteSql, con);
            deleteCommand.Parameters.AddWithValue("@id_dev", id_dev);
            deleteCommand.ExecuteNonQuery();

            string insertSql = @"INSERT INTO BAS_PARAM (ID_DEV, PARAM, STRVALUE) 
                                VALUES (@id_dev, 'LASTEVENT', @eventId)";

            using var insertCommand = new FbCommand(insertSql, con);
            insertCommand.Parameters.AddWithValue("@id_dev", id_dev);
            insertCommand.Parameters.AddWithValue("@eventId", eventId.ToString());
            insertCommand.ExecuteNonQuery();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting last event ID: {ex.Message}");
            return false;
        }
    }

    public long GetLastEventID(int id_dev)
    {
        using var con = CreateConnection();
        try
        {
            con.Open();

            string sql = @"SELECT bp.STRVALUE 
                          FROM bas_param bp 
                          WHERE bp.id_dev = @id_dev 
                          AND bp.param = 'LASTEVENT'";

            using var command = new FbCommand(sql, con);
            command.Parameters.AddWithValue("@id_dev", id_dev);

            var result = command.ExecuteScalar();
            if (result != null && long.TryParse(result.ToString(), out long lastEventId))
            {
                return lastEventId;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting last event ID: {ex.Message}");
            return 0;
        }
    }

    public int? EventInsert(int id_db = 1, int? id_eventtype = null, int? id_cntrl = null,
                           int? id_reader = null, string note = null, DateTime? time = null,
                           int? id_video = null, int? id_user = null, int? ess1 = null,
                           int? ess2 = null, int? idsource = null, long? idserverts = null)
    {
        using var con = CreateConnection(); 
        try
        {
            con.Open();

            string sql = @"EXECUTE PROCEDURE DEVICEEVENTS_INSERT(
            @id_db, @id_eventtype, @id_cntrl, @id_reader, @note, @time, 
            @id_video, @id_user, @ess1, @ess2, @idsource, @idserverts)";

            using var command = new FbCommand(sql, con);

            command.Parameters.AddWithValue("@id_db", id_db);
            command.Parameters.AddWithValue("@id_eventtype", id_eventtype ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@id_cntrl", id_cntrl ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@id_reader", id_reader ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@note", note ?? (object)DBNull.Value);

            string timeString = time?.ToString("yyyy-MM-dd HH:mm:ss") ?? "NOW";
            command.Parameters.AddWithValue("@time", timeString);

            command.Parameters.AddWithValue("@id_video", id_video ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@id_user", id_user ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ess1", ess1 ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ess2", ess2 ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@idsource", idsource ?? 1);
            command.Parameters.AddWithValue("@idserverts", idserverts != null ? (long)(idserverts / 1000) : (object)DBNull.Value);

            // Добавляем выходной параметр
            var outputParam = new FbParameter("@return_value", FbDbType.Integer);
            outputParam.Direction = ParameterDirection.ReturnValue;
            command.Parameters.Add(outputParam);

            command.ExecuteNonQuery();

            // Получаем возвращаемое значение
            return outputParam.Value != DBNull.Value ? Convert.ToInt32(outputParam.Value) : (int?)null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error inserting event: {ex.Message}");
            return null;
        }
    }
    public List<EventType> GetAllEventTypes()
    {
        var eventTypes = new List<EventType>();

        using var con = CreateConnection();
        try
        {
            con.Open();

            string sql = @"SELECT ID_EVENTTYPE, NAME 
                       FROM eventtype 
                       WHERE ACTIVE = 1 
                       ORDER BY ID_EVENTTYPE";

            using var command = new FbCommand(sql, con);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                eventTypes.Add(new EventType
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1)
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting event types: {ex.Message}");
        }

        return eventTypes;
    }

    // Класс для хранения данных о типе события
    public class EventType
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public DataTable GetDevice()
    {
        using var con = CreateConnection();
        con.Open();

        string sql = @"SELECT 
            d.id_dev, 
            bp.intvalue as IP,
            bp_login.strvalue as LOGIN,
            bp_pass.strvalue as PASS,
            d.id_ctrl as ctrl,
            SUBSTRING(d.NAME FROM 1 FOR 31) as CTRL_NAME
        FROM device d
        LEFT JOIN bas_param bp ON d.id_dev = bp.id_dev AND bp.param = 'IP'
        LEFT JOIN bas_param bp_login ON bp_login.id_dev = d.id_dev AND bp_login.param = 'LOGIN'
        LEFT JOIN bas_param bp_pass ON bp_pass.id_dev = d.id_dev AND bp_pass.param = 'PASS'
        WHERE bp.intvalue IS NOT NULL";

        using var getcomand = new FbCommand(sql, con);
        var reader = getcomand.ExecuteReader();
        DataTable table = new DataTable();
        table.Load(reader);
        return table;
    }

    public DataTable GetCardForLoad(int id_dev)
    {
        using var con = CreateConnection();
        con.Open();

        string sql = $@"select cd.id_cardindev, cd.id_card, cd.id_dev,cd.operation from cardindev cd
        join device d on d.id_dev=cd.id_dev
        join device d2 on d2.id_ctrl=d.id_ctrl and d2.id_reader is null
        where d2.id_dev={id_dev}";

        using var getcomand = new FbCommand(sql, con);
        var reader = getcomand.ExecuteReader();
        DataTable table = new DataTable();
        table.Load(reader);
        return table;
    }

    public void DeleteCardInDev(int id_cardindev)
    {
        using var con = CreateConnection();
        con.Open();

        using var getcomand = new FbCommand($@"delete from cardindev cd where cd.id_cardindev ={id_cardindev}", con);
        getcomand.ExecuteNonQuery();
    }

    public void UpdateCardInDevIncrement(int id_cardindev)
    {
        using var con = CreateConnection();
        con.Open();

        using var getcomand = new FbCommand($@"update cardindev cd set cd.attempts=cd.attempts+1 where cd.id_cardindev={id_cardindev}", con);
        getcomand.ExecuteNonQuery();
    }

    public int FixCardIdxOK(string idCard, int idDev, int uid)
    {
        using var con = CreateConnection();
        con.Open();

        try
        {
            // Сначала пытаемся обновить существующую запись
            string updateSql = @"UPDATE CARDIDX SET
                DEVIDX = @uid,
                LOAD_TIME = CURRENT_TIMESTAMP,
                LOAD_RESULT = 'OK'
                WHERE (ID_CARD = @idCard) AND (ID_DEV = @idDev)";

            using var updateCommand = new FbCommand(updateSql, con);
            updateCommand.Parameters.AddWithValue("@uid", uid);
            updateCommand.Parameters.AddWithValue("@idCard", idCard);
            updateCommand.Parameters.AddWithValue("@idDev", idDev);

            int rowsUpdated = updateCommand.ExecuteNonQuery();

            // Если не нашли запись для обновления - вставляем новую
            if (rowsUpdated == 0)
            {
                string insertSql = @"INSERT INTO CARDIDX 
                    (ID_CARD, ID_DEV, DEVIDX, LOAD_TIME, LOAD_RESULT) 
                    VALUES (@idCard, @idDev, @uid, CURRENT_TIMESTAMP, 'OK')";

                using var insertCommand = new FbCommand(insertSql, con);
                insertCommand.Parameters.AddWithValue("@uid", uid);
                insertCommand.Parameters.AddWithValue("@idCard", idCard);
                insertCommand.Parameters.AddWithValue("@idDev", idDev);

                rowsUpdated = insertCommand.ExecuteNonQuery();
            }

            return rowsUpdated;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in FixCardIdxOK: {ex.Message}");
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