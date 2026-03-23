using RestSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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
        public int ctrl;
        public IPAddress ip;
        public string name;
        public string base_url;
        public string login;
        public string password;
        public string token;
        private string hashPassword;//2644256 admin
        public bool is_online; // признак связи: true - усптройство на связи, false - прибор не отвечает
        public string base_url_api;
        public int time_wait = 10;
        public bool is_authenticated;


        public Device(DataRow row, int time_wait)
        {
            this.time_wait = time_wait;
            try
            {

                this.id_dev = Convert.ToInt32(row["id_dev"]);
                this.ctrl = Convert.ToInt32(row["ctrl"]);
                this.name = Convert.ToString(row["ctrl_name"]);


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

        public Device()
        {
            base_url_api = "/api/v1";
        }

        //получить информацию об устройстве без авторизации.
        public async Task<JsonDocument> GetInfo()
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url) });
            var request = new RestRequest("/api/info");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            var get = await restClient.ExecuteGetAsync(request);
            if (get == null || get.Content == null)
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
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
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

        public async Task<RestResponse> SetRemoteAccessServerSettings(bool enabled, string url, bool customServerEnabled)
        {
            RestClient restClient = new RestClient(new RestClientOptions{ Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var settingsJson = new JsonObject()
            {
                ["enabled"] = enabled,
                ["custom_server_api_url"] = url,
                ["custom_server_enabled"] = customServerEnabled
            };

            var request = new RestRequest("access/general/remote/control/settings", Method.Post);
            request.AddBody(settingsJson);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);;
            return await restClient.ExecuteAsync(request);
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

        public async Task<RestResponse> AddIdentifier(string name, string type)
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var cardjson = new JsonObject()
            {
                ["identifier_owner"] = new JsonObject()
                {
                    ["name"] = name,
                    ["type"] = "owner"
                },
                ["identifier_type"] = type,
                ["identifier_number"] = Convert.ToInt64(name).ToString(),
                ["lock"] = "all"
            };
            var request = new RestRequest("access/identifier");
            request.AddBody(cardjson);  //1673
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            return await restClient.ExecutePostAsync(request);
        }

        //public async Task<RestResponse> AddCard(string card)
        //{
        //    RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
        //    var cardjson = new JsonObject()
        //    {
        //        ["identifier_owner"] = new JsonObject()
        //        {
        //            ["name"] = card,
        //            ["type"] = "owner"
        //        },
        //        ["identifier_type"] = "card",
        //        ["identifier_number"] = Convert.ToInt64(card).ToString(),
        //        ["lock"] = "all"
        //    };
        //    var request = new RestRequest("access/identifier");
        //    request.AddBody(cardjson);  //1673
        //    request.AddHeader("Accept", "application/json");
        //    request.AddHeader("Content-Type", "application/json");
        //    request.AddHeader("Authorization", "Bearer " + token);
        //    return await restClient.ExecutePostAsync(request);
        //}

        public async Task<RestResponse> GetInfoUID(int uid)
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var request = new RestRequest("access/identifier/item/" + uid);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            RestResponse get = await restClient.ExecuteGetAsync(request);
            return get;
        }

        public async Task<RestResponse> GetInfoCard(string name, int majorVersion)
        {
            RestClient restClient = new RestClient(new RestClientOptions
            {
                Timeout = TimeSpan.FromSeconds(time_wait),
                BaseUrl = new Uri(base_url + base_url_api)
            });

            var request = new RestRequest("access/identifier/items");

            if (majorVersion >= 2)
            {
                request.AddQueryParameter("filter_field", "identifier_number");
                request.AddQueryParameter("filter_type", "equal");
                request.AddQueryParameter("filter_format", "string");
                request.AddQueryParameter("filter_value", name);
            }
            else
            {
                request.AddQueryParameter("filter", $"identifier_number eq '{name}'");
            }

            request.AddQueryParameter("page_number", "1");
            request.AddQueryParameter("limit", "20");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);

            return await restClient.ExecuteGetAsync(request);
        }

        public async Task<RestResponse> GetEvents(long fromTimestamp = 0, int limit = 50)
        {
            RestClient restClient = new RestClient(new RestClientOptions
            {
                Timeout = TimeSpan.FromSeconds(time_wait),
                BaseUrl = new Uri(base_url + base_url_api)
            });

            var request = new RestRequest("log/items");

            // Добавляем параметры запроса
            request.AddQueryParameter("limit", limit.ToString());
            request.AddQueryParameter("page_number", "1");
            request.AddQueryParameter("locale", "en");
            request.AddQueryParameter("sort_type", "asc"); // от старых к новым

            // ПРАВИЛЬНЫЙ ФОРМАТ ФИЛЬТРА для Bas-IP API
            if (fromTimestamp > 0)
            {
                // Преобразуем timestamp в Unix time в секундах
                request.AddQueryParameter("from", (fromTimestamp/1000).ToString());
            }
            long unixSeconds = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 10800) * 1000;
            request.AddQueryParameter("to", unixSeconds.ToString());
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(token))
            {
                request.AddHeader("Authorization", "Bearer " + token);
            }

            return await restClient.ExecuteGetAsync(request);
        }

        public async Task<RestResponse> SystemClearingLog()
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var request = new RestRequest($@"/system/debug/log");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            RestResponse get = await restClient.ExecuteDeleteAsync(request);
            return get;
        }

        /**27.10.2025 Функция очистки журнала событий**/
        public async Task<RestResponse> ClearingLog()
        {
            RestClient restClient = new RestClient(new RestClientOptions
            {
                Timeout = TimeSpan.FromSeconds(time_wait),
                BaseUrl = new Uri(base_url + base_url_api)
            });

            var request = new RestRequest("system/data/clear", Method.Post);

            // Очищаем только logs
            var data = new JsonObject
            {
                ["clear_fields"] = new JsonArray("logs")
            };

            // ИЗМЕНЕНО: в DLL версии используется "application/json" вместо ContentType.Json
            request.AddStringBody(data.ToJsonString(), "application/json");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);

            return await restClient.ExecutePostAsync(request);
        }

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

        // ***** НОВЫЕ МЕТОДЫ ИЗ DLL ВЕРСИИ *****

        /// <summary>
        /// Установка PIN-кода для гостя
        /// </summary>
        //public async Task<RestResponse> SetPinCode(string code)
        //{
        //    RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });

        //    var pinjson = new JsonObject()
        //    {
        //        ["identifier_owner"] = new JsonObject()
        //        {
        //            ["name"] = code,
        //            ["type"] = "guest"
        //        },
        //        ["identifier_type"] = "inputCode",
        //        ["identifier_number"] = Convert.ToInt64(code).ToString(),
        //        ["lock"] = "all"
        //    };
        //    var request = new RestRequest("access/identifier");
        //    request.AddBody(pinjson);
        //    request.AddHeader("Accept", "application/json");
        //    request.AddHeader("Content-Type", "application/json");
        //    request.AddHeader("Authorization", "Bearer " + token);
        //    return await restClient.ExecutePostAsync(request);
        //}

        /// <summary>
        /// Установка мастер-кода
        /// </summary>
        public async Task<RestResponse> SetMasterCode(string code)
        {
            RestClient restClient = new RestClient(new RestClientOptions { Timeout = TimeSpan.FromSeconds(time_wait), BaseUrl = new Uri(base_url + base_url_api) });
            var mcodejson = new JsonObject()
            {
                ["input_code_enable"] = true,
                ["input_code_number"] = code
            };

            // ИЗМЕНЕНО: в DLL версии нет пробела в начале URL
            var request = new RestRequest("access/general/unlock/input/code");
            request.AddBody(mcodejson);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            return await restClient.ExecutePostAsync(request);
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

                // ИЗМЕНЕНО: в DLL версии используется BitConverter с Replace и ToLowerInvariant
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                // Оригинальная версия для .NET 5+:
                // return Convert.ToHexString(hashBytes);
            }
        }
    }
}