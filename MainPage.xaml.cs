﻿using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Core;
using Windows.Media.SpeechRecognition;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Windows.UI.Xaml.Media;
using Windows.Globalization;
using Windows.UI.Xaml.Documents;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls.Maps;
using Place2Be.Model;

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

        public MainPage()
        {
            this.InitializeComponent();
            isListening = false;
            dictatedTextBuilder = new StringBuilder();
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
                commands = new ObservableCollection<string>();
                commands.Add("zoom in");
                commands.Add("zoom out");
                commands.Add("nearby restaurants");
                ContinuousRecognize();
            }
            else
            {
                this.dictationTextBox.Text = "Permission to access capture resources was not given by the user, reset the application setting in Settings->Privacy->Microphone.";
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
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
                speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }

            this.speechRecognizer = new SpeechRecognizer(recognizerLanguage);
            
            speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;
            
            var dictationConstraint = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "Mapcontrol");
            speechRecognizer.Constraints.Add(dictationConstraint);
            SpeechRecognitionCompilationResult result = await speechRecognizer.CompileConstraintsAsync();
            if (result.Status != SpeechRecognitionResultStatus.Success)
            {
                rootPage.NotifyUser("Grammar Compilation Failed: " + result.Status.ToString(), NotifyType.ErrorMessage);
            }

            // Set timeout timer to 1 day
            TimeSpan timeSpan = new TimeSpan(1, 0, 0, 0);
            speechRecognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout.Add(timeSpan);
            speechRecognizer.Timeouts.InitialSilenceTimeout = timeSpan;
            
            speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;
            speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
            speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;

        }

        private void NotifyUser(string s, object errorMessage)
        {
            Debug.WriteLine(errorMessage + " - " + s);
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
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
                speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }
        }

        private async void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
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
                        rootPage.NotifyUser("Continuous Recognition Completed: " + args.Status.ToString(), NotifyType.StatusMessage);
                        isListening = false;
                    });
                }
            }
        }
        
        private async void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            string hypothesis = args.Hypothesis.Text;

            // Update the textbox with the currently confirmed text, and the hypothesis combined.
            string textboxContent = dictatedTextBuilder.ToString() + " " + hypothesis + " ...";
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                dictationTextBox.Text = textboxContent;
            });
        }
        
        private async void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            if (args.Result.Confidence == SpeechRecognitionConfidence.Medium ||
                args.Result.Confidence == SpeechRecognitionConfidence.High)
            {
                string text = args.Result.Text.ToLower();
                string knownCommand = "UNKNOWN COMMAND - ";
                foreach (String s in commands)
                {
                    if (text.Contains(s.ToLower()))
                    {
                        knownCommand = "";
                        if (text.Contains("zoom in"))
                        {
                            Map.TryZoomInAsync();
                        }
                        else if (text.Contains("zoom out"))
                        {
                            Map.TryZoomOutAsync();
                        }
                        else if (text.Contains("nearby restaurants"))
                        {
                            Debug.WriteLine(RequestManager.GetInstance().RetrieveNearbyPlace(geoposition, "restaurant"));
                        }
                    }
                }
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
                        discardedText = discardedText.Length <= 25 ? discardedText : (discardedText.Substring(0, 25) + "...");

                        discardedTextBlock.Text = "Discarded due to low/rejected Confidence: " + discardedText;
                        discardedTextBlock.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    }
                });
            }
        }
        
        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                rootPage.NotifyUser(args.State.ToString(), NotifyType.StatusMessage);
            });
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
                        if ((uint)ex.HResult == HResultPrivacyStatementDeclined)
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
            //            else
            //            {
            //                isListening = false;
            //
            //                if (speechRecognizer.State != SpeechRecognizerState.Idle)
            //                {
            //                    // Cancelling recognition prevents any currently recognized speech from
            //                    // generating a ResultGenerated event. StopAsync() will allow the final session to 
            //                    // complete.
            //                    try
            //                    {
            //                        await speechRecognizer.ContinuousRecognitionSession.StopAsync();
            //
            //                        // Ensure we don't leave any hypothesis text behind
            //                        dictationTextBox.Text = dictatedTextBuilder.ToString();
            //                    }
            //                    catch (Exception exception)
            //                    {
            //                        var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
            //                        await messageDialog.ShowAsync();
            //                    }
            //                }
            //            }
        }
        
        private void dictationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var grid = (Grid)VisualTreeHelper.GetChild(dictationTextBox, 0);
            for (var i = 0; i <= VisualTreeHelper.GetChildrenCount(grid) - 1; i++)
            {
                object obj = VisualTreeHelper.GetChild(grid, i);
                if (!(obj is ScrollViewer))
                {
                    continue;
                }

                ((ScrollViewer)obj).ChangeView(0.0f, ((ScrollViewer)obj).ExtentHeight, 1.0f);
                break;
            }
        }
        
        private async void openPrivacySettings_Click(Hyperlink sender, HyperlinkClickEventArgs args)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-speechtyping"));
        }
    }

    internal enum NotifyType
    {
        ErrorMessage,
        StatusMessage
    }
}