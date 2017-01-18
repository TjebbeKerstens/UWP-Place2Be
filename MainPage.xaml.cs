using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Core;
using Windows.Media.SpeechRecognition;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Windows.UI.Xaml.Media;
using Windows.Globalization;
using Windows.UI.Xaml.Documents;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.Resources.Core;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Services.Maps;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Input;
using Place2Be.Model;
using Newtonsoft.Json.Linq;

namespace Place2Be
{
    public sealed partial class MainPage : Page
    {
        private MainPage rootPage;

        private CoreDispatcher dispatcher;
        private SpeechRecognizer speechRecognizer;

        private bool isListening;

        private StringBuilder dictatedTextBuilder;

        private static uint HResultPrivacyStatementDeclined = 0x80045509;

        private ObservableCollection<String> commands;

        private Geolocator geolocator;
        private Geoposition geoposition;
        private RequestManager rm;
        public MapControl MapC;
        private ResourceMap speechResourceMap;

        private PointOfInterest currentSelectedPOI;
        private BasicGeoposition currentPosition;
        private ContentDialog currentDialog;

//        public TextBox destinationT

        public TextBox DestinationTextBox
        {
            get { return destinationTextBox; }
            set { destinationTextBox = value; }
        }

        public MediaElement musicElement;
        public MainPage()
        {
//            destinationTB = destinationTextBox;
            MapC = Map;
            this.InitializeComponent();
            isListening = false;
            dictatedTextBuilder = new StringBuilder();
            rm = RequestManager.GetInstance();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = this;

            dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

            geolocator = new Geolocator();
            geolocator.DesiredAccuracyInMeters = 10;
            geolocator.ReportInterval = 0;
            setPositionAsync();

            // Prompt the user for permission to access the microphone. This request will only happen
            // once, it will not re-prompt if the user rejects the permission.
            bool permissionGained = await AudioCapturePermissions.RequestMicrophonePermission();
            if (permissionGained)
            {
                await InitializeRecognizer(SpeechRecognizer.SystemSpeechLanguage);

                ContinuousRecognize();
            }
            else
            {
                this.dictationTextBox.Text =
                    "Permission to access capture resources was not given by the user, reset the application setting in Settings->Privacy->Microphone.";
            }
        }

        private async void setPositionAsync()
        {
            // Get current user position
            geoposition = await geolocator.GetGeopositionAsync();
            string latitude = geoposition.Coordinate.Latitude.ToString("0.0000000000");
            string Longitude = geoposition.Coordinate.Longitude.ToString("0.0000000000");

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

        private async Task InitializeRecognizer(Language recognizerLanguage)
        {
            if (speechRecognizer != null)
            {
                // cleanup prior to re-initializing this scenario.
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;
                speechRecognizer.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated -=
                    ContinuousRecognitionSession_ResultGenerated;
                speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }

            this.speechRecognizer = new SpeechRecognizer(recognizerLanguage);

            speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;

            Uri uri = new Uri("ms-appx:///Strings/srgs.grxml");

            var storageFile =
                await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri);
            var grammarFileConstraint = new SpeechRecognitionGrammarFileConstraint(storageFile, "commands");
            speechRecognizer.Constraints.Add(grammarFileConstraint);

            SpeechRecognitionCompilationResult result = await speechRecognizer.CompileConstraintsAsync();

            if (result.Status != SpeechRecognitionResultStatus.Success)
            {
                rootPage.NotifyUser("Grammar Compilation Failed: " + result.Status.ToString(), NotifyType.ErrorMessage);
            }


            speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;
            speechRecognizer.ContinuousRecognitionSession.ResultGenerated +=
                ContinuousRecognitionSession_ResultGenerated;
            speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
        }

        private void NotifyUser(string s, object errorMessage)
        {
            print(errorMessage + " - " + s);
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (this.speechRecognizer != null)
            {
                if (isListening)
                {
                    await this.speechRecognizer.ContinuousRecognitionSession.CancelAsync();
                    isListening = false;
                }

                dictationTextBox.Text = "";

                speechRecognizer.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated -=
                    ContinuousRecognitionSession_ResultGenerated;
                speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }
        }

        private async void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender,
            SpeechContinuousRecognitionCompletedEventArgs args)
        {
            if (args.Status != SpeechRecognitionResultStatus.Success)
            {
                if (args.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        rootPage.NotifyUser("Automatic Time Out of Dictation", NotifyType.StatusMessage);
                        dictationTextBox.Text = dictatedTextBuilder.ToString();
                        isListening = false;
                    });
                }
                else
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        rootPage.NotifyUser("Continuous Recognition Completed: " + args.Status.ToString(),
                            NotifyType.StatusMessage);
                        isListening = false;
                    });
                }
            }
        }

        private async void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender,
            SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            string hypothesis = args.Hypothesis.Text;

            // Update the textbox with the currently confirmed text, and the hypothesis combined.
            string textboxContent = dictatedTextBuilder.ToString() + " " + hypothesis + " ...";
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { dictationTextBox.Text = textboxContent; });
        }

        private async void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender,
            SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            if (args.Result.Confidence == SpeechRecognitionConfidence.Low ||
                args.Result.Confidence == SpeechRecognitionConfidence.Medium ||
                args.Result.Confidence == SpeechRecognitionConfidence.High)
            {
                string text = args.Result.Text.ToLower();
                string knownCommand = "UNKNOWN COMMAND - ";
                object o = listView1;
                knownCommand = "";
                var dp = CoreApplication.MainView.CoreWindow.Dispatcher;
                if (text.Contains("zoom in"))
                {
                    Map.TryZoomInAsync();
                }
                else if (text.Contains("zoom out"))
                {
                    Map.TryZoomOutAsync();
                }
                else if (text.Contains("near"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        RetrieveNearbyPlace("Restaurant");
                    });
                }
                else if (text.Contains("one") || text.Contains("first"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[0];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("two") || text.Contains("second"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[1];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("three") || text.Contains("third"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[2];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("four") || text.Contains("fourth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[3];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("five") || text.Contains("fifth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[4];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("six") || text.Contains("sixth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[5];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("seven") || text.Contains("seventh"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[6];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("eight") || text.Contains("eitht"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[7];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("nine") || text.Contains("ninth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[8];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("ten") || text.Contains("tenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[9];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("eleven") || text.Contains("eleventh"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[10];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("twelve") || text.Contains("twelfth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[11];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("thirteen") || text.Contains("thirteenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[12];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("fourteen") || text.Contains("fourteenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[13];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("fifteen") || text.Contains("fifteenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[14];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("sixteen") || text.Contains("sixteenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[15];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("seventeen") || text.Contains("seventeenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[16];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("eighteen") || text.Contains("eighteenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[17];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("nineteen") || text.Contains("nineteenth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[18];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("twenty") || text.Contains("twentieth"))
                {
                    await dp.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PointOfInterest poi = (PointOfInterest) listView1.Items[19];
                        OpenPopup(poi);
                    });
                }
                else if (text.Contains("drive"))
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {

                        if (currentSelectedPOI != null)
                        {
                            currentDialog.Hide();
                            showRoute(currentPosition, currentSelectedPOI.Location.Position, rootPage, true,
                                destinationTextBox);

                        }
                        else
                        {
                            destinationTextBox.Text = "No Destination Selected";
                            playMusic();
                        }
                    });
                }
                else if (text.Contains("walk"))
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {

                        if (currentSelectedPOI != null)
                        {

                            currentDialog.Hide();
                            showRoute(currentPosition, currentSelectedPOI.Location.Position, rootPage, false,
                                destinationTextBox);

                        }
                        else
                        {
                            destinationTextBox.Text = "No Destination Selected";
                            playMusic();
                        }
                    });
                }
                else if (text.Contains("cancel"))
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        currentDialog.Hide();
                    });
                }
                //                    } 
                //                }
                dictatedTextBuilder.Append(knownCommand + args.Result.Text + "\n");

                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    discardedTextBlock.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                    dictationTextBox.Text = dictatedTextBuilder.ToString();
                });
            }
            else
            {
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    dictationTextBox.Text = dictatedTextBuilder.ToString();
                    string discardedText = args.Result.Text;
                    if (!string.IsNullOrEmpty(discardedText))
                    {
                        discardedText = discardedText.Length <= 25
                            ? discardedText
                            : (discardedText.Substring(0, 25) + "...");

                        discardedTextBlock.Text = "Discarded due to low/rejected Confidence: " + discardedText;
                        discardedTextBlock.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    }
                });
            }
        }

        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender,
            SpeechRecognizerStateChangedEventArgs args)
        {
            await
                dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () => { rootPage.NotifyUser(args.State.ToString(), NotifyType.StatusMessage); });
        }

        public async void ContinuousRecognize()
        {
            if (isListening == false)
            {
                // The recognizer can only start listening in a continuous fashion if the recognizer is currently idle.
                // This prevents an exception from occurring.
                if (speechRecognizer.State == SpeechRecognizerState.Idle)
                {
                    hlOpenPrivacySettings.Visibility = Visibility.Collapsed;
                    discardedTextBlock.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                    try
                    {
                        isListening = true;
                        await speechRecognizer.ContinuousRecognitionSession.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        if ((uint) ex.HResult == HResultPrivacyStatementDeclined)
                        {
                            // Show a UI link to the privacy settings.
                            hlOpenPrivacySettings.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            var messageDialog = new Windows.UI.Popups.MessageDialog(ex.Message, "Exception");
                            await messageDialog.ShowAsync();
                        }

                        isListening = false;
                    }
                }
            }
        }

        private void dictationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var grid = (Grid) VisualTreeHelper.GetChild(dictationTextBox, 0);
            for (var i = 0; i <= VisualTreeHelper.GetChildrenCount(grid) - 1; i++)
            {
                object obj = VisualTreeHelper.GetChild(grid, i);
                if (!(obj is ScrollViewer))
                {
                    continue;
                }

                ((ScrollViewer) obj).ChangeView(0.0f, ((ScrollViewer) obj).ExtentHeight, 1.0f);
                break;
            }
        }


        private async void openPrivacySettings_Click(Hyperlink sender, HyperlinkClickEventArgs args)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-speechtyping"));
        }

        private async void RetrieveNearbyPlace(string type)
        {
            string json = await rm.RetrieveNearbyPlace(geoposition, type);

            JObject joResponse = JObject.Parse(json);
            JArray results = (JArray) joResponse.GetValue("results");
            List<PointOfInterest> pointList = new List<PointOfInterest>();
            print(results.ToString());

            for (int i = 0; i < results.Count; i++)
            {
                int id = i + 1;
                float lat = (float) results[i]["geometry"]["location"]["lat"];
                float lng = (float) results[i]["geometry"]["location"]["lng"];
                string name = (string) results[i]["name"];
                string address = (string) results[i]["vicinity"];
                var pinUri = new Uri("ms-appx:///Assets/LocationPin.png");

                PointOfInterest poi = new PointOfInterest()
                {
                    Id = id,
                    DisplayName = name,
                    ImageSourceUri = pinUri,
                    Address = address,
                    NormalizedAnchorPoint = new Point(0.5, 1),
                    FullArray = (JObject) results[i],
                    Location = new Geopoint(new BasicGeoposition()
                    {
                        Latitude = lat,
                        Longitude = lng
                    })
                };

                pointList.Add(poi);
            }

            MapItems.ItemsSource = pointList;
            listView1.ItemsSource = pointList;
        }

        private void mapItemClick(object sender, RoutedEventArgs e)
        {
            var buttonSender = sender as Image;
            PointOfInterest poi = buttonSender.DataContext as PointOfInterest;
            rootPage.NotifyUser("PointOfInterest clicked = " + poi.DisplayName, NotifyType.StatusMessage);
            OpenPopup(poi);
        }

        public static async void showRoute(BasicGeoposition start, BasicGeoposition end, MainPage mp, bool driving, TextBox textBox = null)
        {
            MapRouteFinderResult routeResult;
            if (driving)
            {
                routeResult = await MapRouteFinder.GetDrivingRouteAsync(
                    new Geopoint(start),
                    new Geopoint(end),
                    MapRouteOptimization.TimeWithTraffic,
                    MapRouteRestrictions.None);
            }
            else
            {
                routeResult = await MapRouteFinder.GetWalkingRouteAsync(
                    new Geopoint(start),
                    new Geopoint(end));
            }

            if (routeResult.Status == MapRouteFinderStatus.Success)
            {
                // Use the route to initialize a MapRouteView.
                MapRouteView viewOfRoute = new MapRouteView(routeResult.Route);
                viewOfRoute.RouteColor = Colors.Yellow;
                viewOfRoute.OutlineColor = Colors.Black;
                
            
                textBox.Text = "Distance: " + viewOfRoute.Route.LengthInMeters + "m \n";
                textBox.Text += "Travel Time: " + viewOfRoute.Route.EstimatedDuration.Minutes + "m " + viewOfRoute.Route.EstimatedDuration.Seconds + "s \n";
                DateTime eta = DateTime.Now.Add(viewOfRoute.Route.EstimatedDuration);
                textBox.Text += "ETA: " + eta.ToString("HH:mm:ss");
               
                // Add the new MapRouteView to the Routes collection
                // of the MapControl.
//                mp.MapC.Routes.Add(viewOfRoute);
                if (mp.Map.Routes.Count > 0)
                {
                    mp.Map.Routes.RemoveAt(0);
                }
                mp.Map.Routes.Add(viewOfRoute);

                // Fit the MapControl to the route.
                await mp.Map.TrySetViewBoundsAsync(
                    routeResult.Route.BoundingBox,
                    null,
                    MapAnimationKind.Bow);
            }
        }

        private void print(string s)
        {
            Debug.WriteLine("--DEBUG-- " + s);
        }

        private void ListView1_OnItemClick(object sender, ItemClickEventArgs e)
        {
            PointOfInterest poi = (PointOfInterest) e.ClickedItem;
            OpenPopup(poi);
        }


        private async Task<bool> OpenPopup(PointOfInterest poi)
        {
            currentPosition= new BasicGeoposition();
            currentPosition.Latitude = geoposition.Coordinate.Latitude;
            currentPosition.Longitude = geoposition.Coordinate.Longitude;
            currentSelectedPOI = poi;
            LocationDialog ld = new LocationDialog(poi, currentPosition, this);
            currentDialog = ld;
            ld.ShowAsync();
            return true;
        }

        public async Task playMusic()
        {
            musicElement = new MediaElement();
            musicElement.AudioCategory = Windows.UI.Xaml.Media.AudioCategory.Media;

            StorageFolder Folder = Windows.ApplicationModel.Package.Current.InstalledLocation;
            Folder = await Folder.GetFolderAsync("Strings");
            StorageFile sf = await Folder.GetFileAsync("dest_unkown.wav");
            musicElement.SetSource(await sf.OpenAsync(FileAccessMode.Read), sf.ContentType);
            musicElement.Play();
        }
    }

    internal enum NotifyType
    {
        ErrorMessage,
        StatusMessage
    }
}