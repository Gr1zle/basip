// DB.txt - обновленный класс DB с проверкой таблиц
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

        // Проверка всех обязательных таблиц
        public bool CheckRequiredTables()
        {
            string[] requiredTables = {
                "DEVICE",
                "BAS_PARAM",
                "CARDINDEV",
                "CARD"
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

        // Остальные методы класса DB
        public DataTable GetDevice()
        {
            string sql = @"select d.id_dev, bp.intvalue as IP,
                bp4.strvalue as LOGIN,
                bp5.strvalue as PASS
                from device d
                join bas_param bp on d.id_dev=bp.id_dev
                left join bas_param bp4 on bp4.id_dev=d.id_dev  and bp4.param='LOGIN'
                left join bas_param bp5 on bp5.id_dev=d.id_dev  and bp5.param='PASS'
                where bp.param='IP'";

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

        public int FixCardIdxOK(string idCard, int idDev, int uid, FbConnection con)
        {
            try
            {
                // Сначала пытаемся обновить существующую запись
                string updateSql = @"UPDATE CARDIDX SET
            DEVIDX = @uid,
            LOAD_TIME = CURRENT_TIMESTAMP,
            LOAD_RESULT = 'OK'
            WHERE (ID_CARD = @idCard) AND (ID_DEV = @idDev)";

                FbCommand updateCommand = new FbCommand(updateSql, con);
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

                    FbCommand insertCommand = new FbCommand(insertSql, con);
                    insertCommand.Parameters.AddWithValue("@uid", uid);
                    insertCommand.Parameters.AddWithValue("@idCard", idCard);
                    insertCommand.Parameters.AddWithValue("@idDev", idDev);

                    rowsUpdated = insertCommand.ExecuteNonQuery();
                }

                return rowsUpdated;
            }
            catch (Exception ex)
            {
                return 0;
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
    }
}