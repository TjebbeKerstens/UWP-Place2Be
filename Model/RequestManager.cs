using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Web.Http;

namespace Place2Be.Model
{
    public class RequestManager
    {
        private static RequestManager _instance;
        private HttpClient client = new HttpClient();

        string api_key = "AIzaSyB40JmMGhRhBwqzOK-EvTVQ020TvSLPL_I";

        private RequestManager() { }

        public static RequestManager GetInstance()
        {
            if (_instance == null)
            {
                _instance = new RequestManager();
            }

            return _instance;
        }

        public async Task<string> RetrieveNearbyPlace()
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);
            try
            {
                Uri uri = new Uri($"https://maps.googleapis.com/maps/api/place/nearbysearch/");
                HttpResponseMessage response = await client.GetAsync(uri).AsTask(cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return string.Empty;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine(jsonResponse);
                return jsonResponse;
            }

            catch(Exception)
            {
                return string.Empty;
            }
        }
    }
}
