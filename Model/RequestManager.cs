using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Devices.Geolocation;

namespace Place2Be.Model
{
    public class RequestManager
    {
        private static RequestManager _instance;
        private HttpClient client = new HttpClient();

        string baseUrl = "https://maps.googleapis.com/maps/api/place/";
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

      //  https://maps.googleapis.com/maps/api/place/nearbysearch/json?location=51.4894830,5.1343090&radius=1000&sensor=true&types=restaurant&key=AIzaSyB40JmMGhRhBwqzOK-EvTVQ020TvSLPL_I
        public async Task<string> RetrieveNearbyPlace(Geoposition gp, string type)
        {
           
            var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);
            double latitude = gp.Coordinate.Latitude;
            double longitude = gp.Coordinate.Longitude;
            string nearbyUrl = baseUrl + "nearbysearch/json?location=" + latitude + "," + longitude + "&radius=1000&sensor=true&types=" + type + "&key=" + api_key;

            try
            {
                Uri uri = new Uri(nearbyUrl);
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
