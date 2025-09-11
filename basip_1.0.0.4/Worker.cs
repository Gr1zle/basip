using FirebirdSql.Data.FirebirdClient;
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
                logger.LogTrace($@"—тарт итерации");
                try
                {
                    run();//запуск модул€, который запустит асинхронные процессы.
                }
                catch (Exception ex) {
                    logger.LogError("Something crash restart everything");
                    logger.LogError(ex.ToString());
                    continue;
                }
                logger.LogTrace($@"timeout basip: {timeout}");
                await Task.Delay(timeout, stoppingToken);// пауза на указанное в настройках врем€.
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

                // ѕровер€ем наличие об€зательных таблиц
                if (!db.CheckRequiredTables())
                {
                    logger.LogCritical("Required tables are missing in the database. The program will be terminated.");
                    Environment.Exit(1); // «авершаем программу с кодом ошибки
                }
            }
            catch (Exception e)
            {
                logger.LogError("No connect database: " + options.db_config);
                logger.LogError(e.ToString());
                return;
            }
            logger.LogTrace("Ok connect database");

            // ќстальной код метода run()...
            List<Task> tasks = new List<Task>();
            Stopwatch stopwatch = Stopwatch.StartNew();

            DataRowCollection data = db.GetDevice().Rows;
            con.Close();// закрываю подключение, чтобы не плодить коннекты.
            logger.LogDebug("71 «арегистрировано панелей bas-ip: "+data.Count+" шт.");
            logger.LogTrace("70 Start async.");
            foreach (DataRow row in data)
            {
             
                tasks.Add(TaskGet(row));//async Ќачинаю асинхронные процессы. ѕроцесс TaskGet - основной процесс, который записывае и удал€ет карты

            }
                //TaskGet(new Device(row), db).Wait();//not sync
            
            Task.WaitAll(tasks.ToArray());//жду пока все процессы завершатс€.
            logger.LogDebug("time: "+stopwatch.ElapsedMilliseconds);
        }

        /* 12.03.2025 
         * ќсвной процесс работы с панелью
         * 
         * 
         */
        private async Task TaskGet(DataRow row)// в row содержатс€ логин, пароль, id_dev, IP адрес
        {
            //DB db = new DB();
            //FbConnection con = db.DBconnect(options.db_config);
           // con.Open();
            Device dev =new Device(row,options.time_wait_http);
           // dev.base_url = "http://192.168.8.102:8888";

            JsonDocument deviceInfo= await dev.GetInfo();//получили документ со свойствами панели bas-ip, с которой будем работать.
            if (!dev.is_online)// св€зи с панелью нет
            {
                logger.LogDebug($@"device {dev.base_url} ofline");
                return;
            }
            if (!await dev.Auth())
            {
                logger.LogDebug($@"106 ќшибка авторизации дл€ панели IP= {dev.base_url}");
                return;
            }

            //если панель на св€зи, то продолжаю работу.
            DB db = new DB();
            FbConnection con = db.DBconnect(options.db_config);
            con.Open();
            DataRowCollection cardList = db.GetCardForLoad((int)row["id_dev"]).Rows;//получить список записи и удалени€ карт дл€ панели
            logger.LogTrace("Card count: " + cardList.Count);
            foreach (DataRow card in cardList)
            {
                switch ((int)card["operation"])
                {
                    case 1:
                        logger.LogDebug($@"Command destination: writekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} key=""{options.uidtransform(card["id_card"].ToString())}"" AddCard ");
                        RestResponse request = await dev.AddCard(options.uidtransform(card["id_card"].ToString()));
                        switch (request.StatusCode)
                        {
                            case HttpStatusCode.OK:
                                var uid = JsonDocument.Parse(request.Content).RootElement.GetProperty("uid");
                                logger.LogDebug($@"Answer destination: writekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} Answer: OK key=""{options.uidtransform(card["id_card"].ToString())}"" uid={uid}");
                                logger.LogTrace($@"121 Query writekey id_dev={row["id_dev"]} key=""{options.uidtransform(card["id_card"].ToString())}"" BASE_URL {dev.base_url} Answer: {request.StatusCode} {request.Content}");
                                db.DeleteCardInDev((int)card["id_cardindev"]);
                                break;
                            case HttpStatusCode.BadRequest:
                                logger.LogDebug($@"Answer destination: writekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} Answer: OK key=""{options.uidtransform(card["id_card"].ToString())}"" card alredy exist card id");
                                logger.LogTrace($@"126 Query writekey id_dev={row["id_dev"]} key=""{options.uidtransform(card["id_card"].ToString())}"" BASE_URL {dev.base_url} Answer: {request.StatusCode} {request.Content}");
                                db.DeleteCardInDev((int)card["id_cardindev"]);
                                break;
                            default:
                                logger.LogError($@"Answer destination: writekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} Answer: ERR key=""{options.uidtransform(card["id_card"].ToString())}"" add card write false status");
                                logger.LogTrace($@"131 Query writekey id_dev={row["id_dev"]} key=""{options.uidtransform(card["id_card"].ToString())}"" BASE_URL {dev.base_url} Answer: {request.StatusCode} {request.Content}");
                                db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                                break;
                        }
                        break;
                    case 2:// обработка команды на удаление номера из панели bas-ip
                           //tring delcommand = "deletekey id_dev =" + row["id_dev"] + " BASE_URL " + dev.base_url + " key = """ + options.uidtransform(card["id_card"].ToString()) + """";

                        //готовлю строку delcommandlog дл€ удобства вести лог.
                        string delcommandlog = $@"deletekey id_dev ={ row["id_dev"]} BASE_URL { dev.base_url} key = ""{ options.uidtransform(card["id_card"].ToString())}""";

                        // logger.LogDebug($@"Command destination: deletekey id_dev={row["id_dev"]} BASE_URL {dev.base_url} key=""{options.uidtransform(card["id_card"].ToString())}"" GetInfoCard api_version=""{deviceInfo.RootElement.GetProperty("api_version").ToString()}""");
                        
                        logger.LogDebug(delcommandlog + "GetInfoCard " + "api_version=\"" + deviceInfo.RootElement.GetProperty("api_version").ToString() + "\"");
                        
                        //запрашиваю у панели информацию о карте (т.к. дл€ удалени€ надо указать UID)
                        //при запросе учитываю версию API
                        RestResponse? content = await dev.GetInfoCard(options.uidtransform(card["id_card"].ToString()), int.Parse(deviceInfo.RootElement.GetProperty("api_version").ToString().Split('.')[0]));//получаем информацию о карте
                        switch (content.StatusCode)
                        {
                            case HttpStatusCode.OK:// если статус ќ , то ответ получил, и начинаю разбор ответа
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
                                if (jsonlist.Count() == 0)// если ничего не пришло в ответ - значит, и карты в панели нет, что и требуетс€.
                                {
                                    logger.LogDebug($@"{delcommandlog} Answer: OK no card in panael");
                                    logger.LogTrace($@"Query  {delcommandlog} Answer: {content.StatusCode} {content.Content}");
                                    db.DeleteCardInDev((int)card["id_cardindev"]);
                                }
                            break;
                            default:
                                logger.LogError($@"{delcommandlog} Answer: ERR faild GetInfoCard (не удалось получить информацию о карте)");
                                logger.LogTrace($@"Query {delcommandlog} Answer: {content.StatusCode} {content.Content}");
                                db.UpdateCardInDevIncrement((int)card["id_cardindev"]);// удаление не удалось. ƒелаю инкремент попыток
                            break;
                        }
                        break;
                }

            }
            con.Close();    
            


        }
      
       
    }
    }
