using RestSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Basip
{
    public class Device
    {
        public int id_dev;
        public IPAddress ip;
        public string base_url;
        public string login;
        public string password;
        public string token;
        private string hashPassword;//2644256 admin
        public bool is_online; // признак связи: true - усптройство на связи, false - прибор не отвечает
        public string base_url_api;
        public int time_wait=10;
        public bool is_authenticated;


        public Device(DataRow row, int time_wait)
        {
            this.time_wait = time_wait;
            try
            {
                this.id_dev = Convert.ToInt32(row["id_dev"]);

                // Проверяем, что IP не DBNull
                if (row["ip"] != DBNull.Value)
                {
                    byte[] ip_byte = BitConverter.GetBytes((int)row["ip"]);
                    Array.Reverse(ip_byte);
                    ip = new IPAddress(ip_byte);
                    base_url = "http://" + ip.ToString() + ":80";
                }
                else
                {
                    // Если IP нет, просто пропускаем это устройство
                    throw new InvalidOperationException("No IP address");
                }

                // Проверяем логин и пароль на DBNull
                login = (row["login"] != DBNull.Value) ? row["login"].ToString() : string.Empty;

                string passValue = (row["pass"] != DBNull.Value) ? row["pass"].ToString() : string.Empty;
                password = CreateMD5(passValue);
            }
            catch (Exception e)
            {
                // Просто логируем и перебрасываем исключение
                Console.WriteLine($"Error creating Device object for ID {row["id_dev"]}: {e.Message}");
                throw;
            }

            base_url_api = "/api/v1";
        }
        public Device() {  
            base_url_api = "/api/v1";
        }
        
        //получить информацию об устройстве без авторизации.
        public async Task<JsonDocument> GetInfo()
        {
            RestClient restClient=new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl=new Uri(base_url) });
            var request = new RestRequest("/api/info");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            var get = await restClient.ExecuteGetAsync(request);
            if (get == null || get.Content==null)
            {
                this.is_online = false;
                return null;
            }
            this.is_online = true;
            return JsonDocument.Parse(get.Content);
        }

        //попытка авторизации
        public async Task<bool> Auth()
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url+base_url_api) });
            var request = new RestRequest($@"/login?username={login}&password={password}");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            var get = await restClient.ExecuteGetAsync(request);
            if (get == null || get.Content == null)
            {
                this.is_online = false;
                return false;
            }
            try
            {
                token = JsonDocument.Parse(get.Content).RootElement.GetProperty("token").ToString();

            }
            catch (Exception ex)
            {
                return false;
            }
            this.is_online = true;

            if (!string.IsNullOrEmpty(token))
            {
                this.is_authenticated = true;  // Отмечаем как авторизованный
                return true;
            }
            return false;
        }

        public async Task<RestResponse> DeleteCard(string uid) 
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var request = new RestRequest($@"access/identifier/item/{uid}");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            RestResponse get = await restClient.ExecuteDeleteAsync(request);
            return get;
        }

        public async Task<RestResponse> AddCard(string card) {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var cardjson = new JsonObject()
            {
                ["identifier_owner"] = new JsonObject()
                {
                    ["name"] = card,
                    ["type"] = "owner"
                },
                ["identifier_type"] = "card",
                ["identifier_number"] = Convert.ToInt64(card).ToString(),
                ["lock"] = "all"
            };
            var request=new RestRequest("access/identifier");
            request.AddBody(cardjson);  //1673
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            return await restClient.ExecutePostAsync(request);
        }

        public async Task<RestResponse> GetInfoUID(int uid)
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var request = new RestRequest("access/identifier/item/"+uid);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            RestResponse get = await restClient.ExecuteGetAsync(request);
            return get;
        }

        public async Task<RestResponse> GetInfoCard(string name, int apiversion)
        {
            RestClient restClient = new RestClient(new RestClientOptions
            {
                Timeout = TimeSpan.FromSeconds(time_wait),
                BaseUrl = new Uri(base_url + base_url_api)
            });

            var request = new RestRequest("access/identifier/items");

            // Добавляем параметры фильтра в URL
            request.AddQueryParameter("filter_field", "identifier_number");
            request.AddQueryParameter("filter_type", "equal");
            request.AddQueryParameter("filter_format", "string");
            request.AddQueryParameter("filter_value", name);

            // Также можно добавить пагинацию чтобы получить точное совпадение
            request.AddQueryParameter("page_number", "1");
            request.AddQueryParameter("limit", "10");

            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);

            return await restClient.ExecuteGetAsync(request);
        }

        //public async Task<RestResponse> GetLogs(int limit = 10, int pageNumber = 1, string locale = "en", string sortType = "asc", long fromTime = 0, long toTime = 0)
        //{
        //    RestClient restClient = new RestClient(new RestClientOptions
        //    {
        //        Timeout = TimeSpan.FromSeconds(time_wait),
        //        BaseUrl = new Uri(base_url + base_url_api)
        //    });

        //    var request = new RestRequest("log/items");

        //    // Добавляем параметры запроса
        //    request.AddQueryParameter("limit", limit.ToString());
        //    request.AddQueryParameter("page_number", pageNumber.ToString());
        //    request.AddQueryParameter("locale", locale);
        //    request.AddQueryParameter("sort_type", sortType);

        //    if (fromTime > 0)
        //        request.AddQueryParameter("from", fromTime.ToString());

        //    if (toTime > 0)
        //        request.AddQueryParameter("to", toTime.ToString());

        //    request.AddHeader("Accept", "application/json");
        //    request.AddHeader("Content-Type", "application/json");

        //    if (!string.IsNullOrEmpty(token))
        //    {
        //        request.AddHeader("Authorization", "Bearer " + token);
        //    }

        //    return await restClient.ExecuteGetAsync(request);
        //}

        public async Task<bool> Logout()
        {
            try
            {
                // Создаем клиента для API v1
                RestClient restClient = new RestClient(new RestClientOptions
                {
                    Timeout = TimeSpan.FromSeconds(time_wait),
                    BaseUrl = new Uri(base_url + base_url_api)  // http://192.168.1.100:8888/api/v1
                });

                // Создаем запрос на logout
                var request = new RestRequest("/logout", Method.Post);
                request.AddHeader("Accept", "application/json");
                request.AddHeader("Content-Type", "application/json");

                // Добавляем токен в заголовок Authorization
                if (!string.IsNullOrEmpty(token))
                {
                    request.AddHeader("Authorization", $"Bearer {token}");
                }

                // Выполняем запрос
                var response = await restClient.ExecutePostAsync(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Очищаем токен после выхода
                    token = null;
                    this.is_authenticated = false;

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        //сделать хэш пароля
        // https://stackoverflow.com/questions/11454004/calculate-a-md5-hash-from-a-string
        public static string CreateMD5(string input)
        {
            // Use input string to calculate MD5 hash
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                return Convert.ToHexString(hashBytes); // .NET 5 +

                // Convert the byte array to hexadecimal string prior to .NET 5
                // StringBuilder sb = new System.Text.StringBuilder();
                // for (int i = 0; i < hashBytes.Length; i++)
                // {
                //     sb.Append(hashBytes[i].ToString("X2"));
                // }
                // return sb.ToString();
            }
        }

    }
}
