using FirebirdSql.Data.FirebirdClient;
using Microsoft.Extensions.Logging;
using RestSharp;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Basip
{
    public class Worker : BackgroundService
    {
        public readonly ILogger logger;
        private WorkerOptions options;
        public TimeSpan timeout;
        public TimeSpan timestart;
        public TimeSpan deltasleep;
        public Worker(ILogger<Worker> logger, WorkerOptions options)
        {
            this.logger = logger;
            this.options = options;
            var time = options.timeout.Split(':');
            timeout = new TimeSpan(Int32.Parse(time[0]), Int32.Parse(time[1]), Int32.Parse(time[2]));
            time = options.timeout.Split(':');
            timestart = new TimeSpan(Int32.Parse(time[0]), Int32.Parse(time[1]), Int32.Parse(time[2]));
            var now = new TimeSpan(DateTime.Now.TimeOfDay.Hours, DateTime.Now.TimeOfDay.Minutes, DateTime.Now.TimeOfDay.Seconds);
            deltasleep = (options.run_now) ? TimeSpan.Zero :
                (timestart >= now) ? timestart - now : timestart - now + new TimeSpan(1, 0, 0, 0);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // System.Reflection.Assembly executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            //var fieVersionInfo = FileVersionInfo.GetVersionInfo(executingAssembly.Location);
            //var version = fieVersionInfo.FileVersion;


            //logger.LogTrace(@$"32 basip start: {timestart} deltasleep: {deltasleep} fieVersionInfo = {fieVersionInfo} version = {version}");

            logger.LogTrace(@$"32 basip start: {timestart} deltasleep: {deltasleep}");
            logger.LogTrace(@$"33 Service basip write and delete card started");
            await Task.Delay(deltasleep);
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogTrace($@"Старт итерации");
                try
                {
                    run();//запуск модуля, который запустит асинхронные процессы.
                }
                catch (Exception ex)
                {
                    logger.LogError("Something crash restart everything");
                    logger.LogError(ex.ToString());
                    continue;
                }
                logger.LogTrace($@"timeout basip: {timeout}");
                await Task.Delay(timeout, stoppingToken);// пауза на указанное в настройках время.
            }
            logger.LogTrace(@$"49 basip stop");
        }
        private void run()
        {
            DB db = new DB();
            FbConnection con = db.DBconnect(options.db_config);
            try
            {
                con.Open();

                // Проверяем наличие обязательных таблиц
                if (!db.CheckRequiredTables())
                {
                    logger.LogCritical("Required tables are missing in the database. The program will be terminated.");
                    Environment.Exit(1); // Завершаем программу с кодом ошибки
                }
            }
            catch (Exception e)
            {
                logger.LogError("No connect database: " + options.db_config);
                logger.LogError(e.ToString());
                return;
            }
            logger.LogTrace("Ok connect database");


            List<Task> tasks = new List<Task>();
            Stopwatch stopwatch = Stopwatch.StartNew();

            DataRowCollection data = db.GetDevice().Rows;
            con.Close();// закрываю подключение, чтобы не плодить коннекты.
            logger.LogDebug("71 Зарегистрировано панелей bas-ip: " + data.Count + " шт.");

            int validDevicesCount = 0;
            logger.LogTrace("70 Start async.");
            foreach (DataRow row in data)
            {
                try
                {
                    // Дополнительная проверка перед созданием устройства
                    if (row["IP"] == DBNull.Value || Convert.ToInt32(row["IP"]) == 0)
                    {
                        logger.LogDebug($"Skipping device ID {row["id_dev"]} - no IP address");
                        continue;
                    }

                    Device device = new Device(row, options.time_wait_http);
                    validDevicesCount++;

                    // Создаем задачу для устройства
                    tasks.Add(TaskGet(row));
                }
                catch (Exception ex)
                {
                    logger.LogDebug($"Failed to create device from row ID {row["id_dev"]}: {ex.Message}");
                    continue;
                }
            }

            logger.LogInformation($"Processing {validDevicesCount} devices with valid IP addresses");

            if (tasks.Count > 0)
            {
                Task.WaitAll(tasks.ToArray());
            }
            else
            {
                logger.LogInformation("No devices with valid IP addresses found");
                return; // Прерываем выполнение если нет валидных устройств
            }
            //TaskGet(new Device(row), db).Wait();//not sync

            Task.WaitAll(tasks.ToArray());//жду пока все процессы завершатся.
            //logger.LogDebug("104 time: " + stopwatch.ElapsedMilliseconds);
        }

        /* 12.03.2025 
         * Освной процесс работы с панелью
         * 
         * 
         */
        private async Task TaskGet(DataRow row)// в row содержатся логин, пароль, id_dev, IP адрес
        {
            //DB db = new DB();
            //FbConnection con = db.DBconnect(options.db_config);
            // con.Open();
            Device dev = new Device(row, options.time_wait_http);
            // dev.base_url = "http://192.168.8.102:8888";

            JsonDocument deviceInfo = await dev.GetInfo();//получили документ со свойствами панели bas-ip, с которой будем работать.
            if (!dev.is_online)// связи с панелью нет
            {
                logger.LogDebug($@"device {dev.base_url} ofline");
                return;
            }

            if (!await dev.Auth())
            {
                logger.LogDebug($@"106 Ошибка авторизации для панели IP= {dev.base_url}");
                return;
            }

            //если панель на связи, то продолжаю работу.
            DB db = new DB();
            FbConnection con = db.DBconnect(options.db_config);
            con.Open();

            DataRowCollection cardList = db.GetCardForLoad((int)row["id_dev"]).Rows;//получить список записи и удаления карт для панели

            logger.LogDebug($"Панель ID: {row["id_dev"]}, IP: {dev.ip} - Card count: " + cardList.Count);

            foreach (DataRow card in cardList)
            {
                switch ((int)card["operation"])
                {
                    case 1:
                        logger.LogDebug($@"Command destination: writekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} key=""{options.uidtransform(card["id_card"].ToString())}"" AddCard ");
                        RestResponse request = await dev.AddCard(options.uidtransform(card["id_card"].ToString()));

                        bool shouldDeleteFromQueue = false;
                        string cardId = card["id_card"].ToString();
                     //   int deviceId = (int)row["id_dev"];
                        int deviceId = (int)card["id_dev"];

                        switch (request.StatusCode)
                        {
                            case HttpStatusCode.OK:
                                var uid = JsonDocument.Parse(request.Content).RootElement.GetProperty("uid").ToString();
                                logger.LogDebug($@"Answer destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} Answer: OK key=""{options.uidtransform(cardId)}"" uid={uid}");

                                try
                                {
                                    // Пробуем распарсить UID, если не получается - используем 0
                                    int uidInt = 0;
                                    if (!int.TryParse(uid, out uidInt))
                                    {
                                        logger.LogWarning($"Cannot parse UID '{uid}' as integer, using 0");
                                        uidInt = 0;
                                    }

                                    int rowsUpdated = db.FixCardIdxOK(
                                        cardId,
                                        deviceId,
                                        uidInt,  // Используем распарсенное значение
                                        con
                                    );

                                    if (rowsUpdated > 0)
                                    {
                                        logger.LogDebug($"162 Successfully updated {rowsUpdated} rows in CARDIDX");
                                    }
                                    else
                                    {
                                        logger.LogWarning($"No rows updated in CARDIDX for card {cardId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError($"Error updating CARDIDX: {ex.Message}");

                                    // Даже при ошибке пытаемся записать хотя бы с uid=0
                                    try
                                    {
                                        db.FixCardIdxOK(cardId, deviceId, 0, con);
                                    }
                                    catch { }
                                }
                                shouldDeleteFromQueue = true;
                                break;

                            case HttpStatusCode.BadRequest:
                                logger.LogDebug($@"Answer destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} Answer: BAD REQUEST key=""{options.uidtransform(cardId)}"" card already exists");

                                try
                                {
                                    // Для существующих карт получаем их UID через API
                                    var cardInfoResponse = await dev.GetInfoCard(options.uidtransform(cardId), 1);
                                    int existingUid = 0;
                                    //logger.LogDebug($"122 cardInfoResponse = {cardInfoResponse.Content}");


                                    if (cardInfoResponse.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(cardInfoResponse.Content))
                                    {
                                        try
                                        {
                                            var cardInfo = JsonDocument.Parse(cardInfoResponse.Content);
                                            if (cardInfo.RootElement.GetProperty("items").GetArrayLength() > 0)
                                            {
                                                var firstItem = cardInfo.RootElement.GetProperty("items")[0];
                                                var uidStr = firstItem.GetProperty("uid").ToString();
                                                if (int.TryParse(uidStr, out existingUid))
                                                {
                                                    logger.LogDebug($"Found existing card UID: {existingUid}");
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.LogWarning($"Could not get UID for existing card: {ex.Message}");
                                        }
                                    }

                                    int rowsUpdated = db.FixCardIdxOK(
                                        cardId,
                                        deviceId,
                                        existingUid,  // Используем реальный UID или 0
                                        con
                                    );

                                    if (rowsUpdated > 0)
                                    {
                                        logger.LogDebug($"Successfully updated {rowsUpdated} rows in CARDIDX (card already exists)");
                                    }
                                    else
                                    {
                                        logger.LogWarning($"No rows updated in CARDIDX for existing card {cardId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError($"Error updating CARDIDX for existing card: {ex.Message}");

                                    // Фолбэк: записываем с uid=0
                                    try
                                    {
                                        db.FixCardIdxOK(cardId, deviceId, 0, con);
                                    }
                                    catch { }
                                }
                                shouldDeleteFromQueue = true;
                                break;

                            default:
                                // Для всех других статусов (ошибки) - инкрементируем счетчик попыток
                                logger.LogWarning($@"Answer destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} Answer: {request.StatusCode} key=""{options.uidtransform(cardId)}""");
                                db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                                break;
                        }

                        // УДАЛЯЕМ из очереди ТОЛЬКО если операция успешна или карта уже существует
                        if (shouldDeleteFromQueue)
                        {
                            db.DeleteCardInDev((int)card["id_cardindev"]);
                            logger.LogInformation($"Card {cardId} successfully processed and removed from queue for device {deviceId}");
                        }
                        break;

                    case 2:// обработка команды на удаление номера из панели bas-ip
                           //tring delcommand = "deletekey id_dev =" + row["id_dev"] + " BASE_URL " + dev.base_url + " key = """ + options.uidtransform(card["id_card"].ToString()) + """";

                        //готовлю строку delcommandlog для удобства вести лог.
                        string delcommandlog = $@"deletekey id_dev ={row["id_dev"]} BASE_URL {dev.base_url} key = ""{options.uidtransform(card["id_card"].ToString())}""";

                        // logger.LogDebug($@"Command destination: deletekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} key=""{options.uidtransform(card["id_card"].ToString())}"" GetInfoCard api_version=""{deviceInfo.RootElement.GetProperty("api_version").ToString()}""");

                        logger.LogDebug(delcommandlog + "GetInfoCard " + "api_version=\"" + deviceInfo.RootElement.GetProperty("api_version").ToString() + "\"");

                        //запрашиваю у панели информацию о карте (т.к. для удаления надо указать UID)
                        //при запросе учитываю версию API
                        RestResponse? content = await dev.GetInfoCard(options.uidtransform(card["id_card"].ToString()), int.Parse(deviceInfo.RootElement.GetProperty("api_version").ToString().Split('.')[0]));//получаем информацию о карте
                        switch (content.StatusCode)
                        {
                            case HttpStatusCode.OK:// если статус ОК, то ответ получил, и начинаю разбор ответа
                                JsonElement.ArrayEnumerator jsonlist = JsonDocument.Parse(content.Content).RootElement.GetProperty("list_items").EnumerateArray();//ищем uid карты

                                foreach (JsonElement element in jsonlist)
                                {

                                    //извлекаю UID карты
                                    string uid_card = element.GetProperty("identifier_uid").ToString();


                                    // logger.LogDebug($@"Command destination: deletekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} key=""{options.uidtransform(card["id_card"].ToString())}"" uid={uid_card} DeleteCard ");
                                    logger.LogDebug(delcommandlog + $@" uid ={uid_card} DeleteCard ");
                                    var status = (await dev.DeleteCard(uid_card)).StatusCode;//удаление карты
                                    switch (status)
                                    {
                                        case HttpStatusCode.OK:
                                            logger.LogDebug($@"{delcommandlog}   Answer: OK uid={uid_card}");
                                            logger.LogTrace($@"152 Query {delcommandlog}  Answer: OK uid={uid_card} {content.StatusCode} {content.Content}");
                                            db.DeleteCardInDev((int)card["id_cardindev"]);
                                            break;
                                        default:
                                            logger.LogDebug($@"Answer destination: {delcommandlog} Answer: ERR uid={uid_card} no delete");
                                            logger.LogTrace($@"157 Query {delcommandlog}  Answer: ERR {content.StatusCode} {content.Content}");
                                            db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                                            break;
                                    }
                                }
                                if (jsonlist.Count() == 0)// если ничего не пришло в ответ - значит, и карты в панели нет, что и требуется.
                                {
                                    logger.LogDebug($@"{delcommandlog} Answer: OK no card in panael");
                                    logger.LogTrace($@"Query  {delcommandlog} Answer: {content.StatusCode} {content.Content}");
                                    db.DeleteCardInDev((int)card["id_cardindev"]);
                                }
                                break;
                            default:
                                logger.LogError($@"{delcommandlog} Answer: ERR faild GetInfoCard (не удалось получить информацию о карте)");
                                logger.LogTrace($@"Query {delcommandlog} Answer: {content.StatusCode} {content.Content}");
                                db.UpdateCardInDevIncrement((int)card["id_cardindev"]);// удаление не удалось. Делаю инкремент попыток
                                break;
                        }
                        break;
                }
            }
            try
            {
                if (dev.is_online)
                {
                    //bool logoutResult = await dev.Logout();
                    //if (logoutResult)
                    //{
                    //    logger.LogTrace($"276 Logout successful for device {dev.base_url}");
                    //}
                    //else
                    //{
                    //    logger.LogWarning($"280 Logout failed for device {dev.base_url}");
                    //}
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Logout exception for device {dev.base_url}: {ex.Message}");
            }
            finally
            {
                con.Close();
            }
        }
    }
}
