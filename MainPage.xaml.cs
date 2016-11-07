using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.SpeechRecognition;
using Windows.Storage.BulkAccess;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Place2Be
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        Geolocator geolocator;
        Geoposition geoposition;
        public MainPage()
        {
            this.InitializeComponent();

            geolocator = new Geolocator();
            geolocator.DesiredAccuracyInMeters = 10;
            geolocator.ReportInterval = 0;

            setPositionAsync();
        }

        private async void setPositionAsync()
        {
            // Get current user position
            geoposition = await geolocator.GetGeopositionAsync();
            string latitude = geoposition.Coordinate.Latitude.ToString("0.0000000000");
            string Longitude = geoposition.Coordinate.Longitude.ToString("0.0000000000");
            string Accuracy = geoposition.Coordinate.Accuracy.ToString("0.0000000000");

            // Write position to usable format
            BasicGeoposition basicGeoposition = new BasicGeoposition();
            basicGeoposition.Latitude = Convert.ToDouble(latitude);
            basicGeoposition.Longitude = Convert.ToDouble(Longitude);

            // Create geopoint for position
            Geopoint current = new Geopoint(basicGeoposition);
            
            // Calculate bounding box containing everything
            GeoboundingBox geoboundingBox = new GeoboundingBox(basicGeoposition, basicGeoposition);

            // Zoom to current position
            await Map.TrySetViewAsync(current);
            Map.TryZoomToAsync(16);

            // Create mapicon
            MapIcon mapIcon = new MapIcon();
            mapIcon.Image = RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/MapPin.png"));
            mapIcon.Location = current;
            mapIcon.NormalizedAnchorPoint = new Point(0.5, 0.5);

            // Wait for the animation to finish before adding the mp
            await Task.Delay(1000);
            Map.MapElements.Add(mapIcon);
        }

        private async void OpenUI_Click(object sender, RoutedEventArgs e)
        {
            SpeechRecognizer recognizer = new SpeechRecognizer();

            SpeechRecognitionTopicConstraint topicConstraint
                    = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "Location");

            recognizer.Constraints.Add(topicConstraint);
            await recognizer.CompileConstraintsAsync(); // Required

            try
            {
                // Open the UI.
                var results = await recognizer.RecognizeWithUIAsync();
                if (results.Confidence != SpeechRecognitionConfidence.Rejected)
                {
                    String result = toLowerCase(results.Text);
                    Debug.WriteLine(result);
                    // No need to call 'Voice.Say'. The control speaks itself.
                    
                    if (result.Contains(toLowerCase("zoom out")))
                    {
                        Map.TryZoomOutAsync();
                    } else if (result.Contains(toLowerCase("zoom in")))
                    {
                        Map.TryZoomInAsync();
                    }
                }
                else
                {
                    Debug.WriteLine("Sorry, I did not get that.");
                }
            }
            catch
            {
                MessageDialog dialog = new MessageDialog("Please go to Settings > Speech and configure your microphone for Speech recognition", "Speech recoginition not set up");
                dialog.ShowAsync();
            }
        }

        private string toLowerCase(string s)
        {
            string result = "";
            foreach (char c in s)
            {
                result += char.ToLower(c);
            }
            return result;
        }
    }
}
