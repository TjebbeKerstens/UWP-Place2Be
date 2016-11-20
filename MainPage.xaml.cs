using Windows.UI.Xaml;
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

namespace Place2Be
{
    public sealed partial class MainPage : Page
    {
        // Reference to the main sample page in order to post status messages.
        private MainPage rootPage;

        // Speech events may come in on a thread other than the UI thread, keep track of the UI thread's
        // dispatcher, so we can update the UI in a thread-safe manner.
        private CoreDispatcher dispatcher;

        // The speech recognizer used throughout this sample.
        private SpeechRecognizer speechRecognizer;

        // Keep track of whether the continuous recognizer is currently running, so it can be cleaned up appropriately.
        private bool isListening;

        // Keep track of existing text that we've accepted in ContinuousRecognitionSession_ResultGenerated(), so
        // that we can combine it and Hypothesized results to show in-progress dictation mid-sentence.
        private StringBuilder dictatedTextBuilder;

        /// <summary>
        /// This HResult represents the scenario where a user is prompted to allow in-app speech, but 
        /// declines. This should only happen on a Phone device, where speech is enabled for the entire device,
        /// not per-app.
        /// </summary>
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

        /// <summary>
        /// Upon entering the scenario, ensure that we have permissions to use the Microphone. This may entail popping up
        /// a dialog to the user on Desktop systems. Only enable functionality once we've gained that permission in order to 
        /// prevent errors from occurring when using the SpeechRecognizer. If speech is not a primary input mechanism, developers
        /// should consider disabling appropriate parts of the UI if the user does not have a recording device, or does not allow 
        /// audio input.
        /// </summary>
        /// <param name="e">Unused navigation parameters</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = this;

            // Keep track of the UI thread dispatcher, as speech events will come in on a separate thread.
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

        /// <summary>
        /// Initialize Speech Recognizer and compile constraints.
        /// </summary>
        /// <param name="recognizerLanguage">Language to use for the speech recognizer</param>
        /// <returns>Awaitable task.</returns>
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

            // Provide feedback to the user about the state of the recognizer. This can be used to provide visual feedback in the form
            // of an audio indicator to help the user understand whether they're being heard.
            speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;


            // Apply the dictation topic constraint to optimize for dictated freeform speech.
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

            // Handle continuous recognition events. Completed fires when various error states occur. ResultGenerated fires when
            // some recognized phrases occur, or the garbage rule is hit. HypothesisGenerated fires during recognition, and
            // allows us to provide incremental feedback based on what the user's currently saying.
            speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;
            speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
            speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;

        }

        private void NotifyUser(string s, object errorMessage)
        {
            Debug.WriteLine(errorMessage + " - " + s);
        }

        /// <summary>
        /// Upon leaving, clean up the speech recognizer. Ensure we aren't still listening, and disable the event 
        /// handlers to prevent leaks.
        /// </summary>
        /// <param name="e">Unused navigation parameters.</param>
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

        /// <summary>
        /// Handle events fired when error conditions occur, such as the microphone becoming unavailable, or if
        /// some transient issues occur.
        /// </summary>
        /// <param name="sender">The continuous recognition session</param>
        /// <param name="args">The state of the recognizer</param>
        private async void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            if (args.Status != SpeechRecognitionResultStatus.Success)
            {
                // If TimeoutExceeded occurs, the user has been silent for too long. We can use this to 
                // cancel recognition if the user in dictation mode and walks away from their device, etc.
                // In a global-command type scenario, this timeout won't apply automatically.
                // With dictation (no grammar in place) modes, the default timeout is 20 seconds.
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

        /// <summary>
        /// While the user is speaking, update the textbox with the partial sentence of what's being said for user feedback.
        /// </summary>
        /// <param name="sender">The recognizer that has generated the hypothesis</param>
        /// <param name="args">The hypothesis formed</param>
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

        /// <summary>
        /// Handle events fired when a result is generated. Check for high to medium confidence, and then append the
        /// string to the end of the stringbuffer, and replace the content of the textbox with the string buffer, to
        /// remove any hypothesis text that may be present.
        /// </summary>
        /// <param name="sender">The Recognition session that generated this result</param>
        /// <param name="args">Details about the recognized speech</param>
        private async void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            // We may choose to discard content that has low confidence, as that could indicate that we're picking up
            // noise via the microphone, or someone could be talking out of earshot.
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
                // In some scenarios, a developer may choose to ignore giving the user feedback in this case, if speech
                // is not the primary input mechanism for the application.
                // Here, just remove any hypothesis text by resetting it to the last known good.
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

        /// <summary>
        /// Provide feedback to the user based on whether the recognizer is receiving their voice input.
        /// </summary>
        /// <param name="sender">The recognizer that is currently running.</param>
        /// <param name="args">The current state of the recognizer.</param>
        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                rootPage.NotifyUser(args.State.ToString(), NotifyType.StatusMessage);
            });
        }

        /// <summary>
        /// Begin recognition, or finish the recognition session. 
        /// </summary>
        /// <param name="sender">The button that generated this event</param>
        /// <param name="e">Unused event details</param>
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

        /// <summary>
        /// Automatically scroll the textbox down to the bottom whenever new dictated text arrives
        /// </summary>
        /// <param name="sender">The dictation textbox</param>
        /// <param name="e">Unused text changed arguments</param>
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

        /// <summary>
        /// Open the Speech, Inking and Typing page under Settings -> Privacy, enabling a user to accept the 
        /// Microsoft Privacy Policy, and enable personalization.
        /// </summary>
        /// <param name="sender">Ignored</param>
        /// <param name="args">Ignored</param>
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