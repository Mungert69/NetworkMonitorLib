using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Utils.Helpers
{
    public class APIHelper
    {
        public static async Task<TResultObj<T?>> GetDataFromResultObjJson<T>(string url) where T : class
        {
            TResultObj<T?> result = new TResultObj<T?>();
            try
            {
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                T? resultData = null;
                string? resultDataStr = JsonUtils.GetStringFieldFromJson(responseBody, "data");

                if (resultDataStr != null)
                {
                    resultData = JsonUtils.GetJsonObjectFromString<T>(resultDataStr);
                }

                string? resultMessage = JsonUtils.GetStringFieldFromJson(responseBody, "message");
                bool? resultSuccess = JsonUtils.GetBooleanFieldFromJson(responseBody, "success");
                result.Data = resultData;
                result.Message = resultMessage ?? "";
                result.Success = resultSuccess ?? false;
                //result=JsonUtils.GetJsonObjectFromString<TResultObj<T>>(responseBody) ?? throw new ArgumentNullException("ResponseToJson");
                //result = JsonSerializer.Deserialize<TResultObj<T>>(responseBody) ?? throw new ArgumentNullException("ResponseToJson");

            }
            catch (Exception ex)
            {
                result.Message += "Error in APIHelper.GetJson getting load server : Error was : " + ex.Message;
                result.Success = false;
            }
            return result;

        }
        public static async Task<TResultObj<T>> GetJson<T>(string url)
        {
            TResultObj<T> result = new TResultObj<T>();
            try
            {
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                result = JsonUtils.GetJsonObjectFromString<TResultObj<T>>(responseBody) ?? throw new ArgumentNullException("ResponseToJson");
                //result = JsonSerializer.Deserialize<TResultObj<T>>(responseBody) ?? throw new ArgumentNullException("ResponseToJson");

            }
            catch (Exception ex)
            {
                result.Message += "Error in APIHelper.GetJson getting load server : Error was : " + ex.Message;
                result.Success = false;
            }
            return result;

        }

        public static async Task<ResultObj> GetJsonResult(string url)
        {
            ResultObj result = new ResultObj();
            try
            {
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                int? data = JsonUtils.GetIntFieldFromJson(responseBody, "data");
                result.Message = $"Got load {data}";
                result.Success = true;
                result.Data = data;
                //result = JsonSerializer.Deserialize<TResultObj<T>>(responseBody) ?? throw new ArgumentNullException("ResponseToJson");

            }
            catch (Exception ex)
            {
                result.Message += "Error in APIHelper.GetJson getting load server : Error was : " + ex.Message;
                result.Success = false;
            }
            return result;

        }


     

    }


}