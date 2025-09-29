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
        
        
        public Device(DataRow row,int time_wait)
        {
           
            this.time_wait = time_wait;
            try
            {
                byte[] ip_byte = BitConverter.GetBytes((int)row["ip"]);
                Array.Reverse(ip_byte);
                ip = new IPAddress(ip_byte);
                login = row["login"].ToString();
                password = CreateMD5(row["pass"].ToString());
            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            base_url = "http://" + ip.ToString() + ":80";
            base_url_api = "/api/v1";
            //       id_dev = (int)row["id_dev"];
            //       id_ctrl = row["id_ctrl"].ToString();
    
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
        public async Task<RestResponse> GetInfoCard(string name,int apiversion)
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var request =(apiversion==1) ? new RestRequest($@"access/identifier/items?page_number=1&limit=20&filter=identifier_number eq '{name}'") 
            : new RestRequest("access/identifier/items?filter_field=identifier_number&filter_type=equal&filter_format=string&filter_value=" + name);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            RestResponse get = await restClient.ExecuteGetAsync(request);
            return get;
        }
        // В класс Device добавь этот метод
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
                // Без logger - просто возвращаем false при ошибке
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
