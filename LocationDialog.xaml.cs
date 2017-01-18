using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Content Dialog item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace Place2Be
{
    public sealed partial class LocationDialog : ContentDialog
    {
        private PointOfInterest poi;
        private BasicGeoposition current;
        private MainPage mp;
        public LocationDialog(PointOfInterest _poi, BasicGeoposition _current, MainPage mainPage)
        {
            this.InitializeComponent();
            poi = _poi;
            current = _current;
            mp = mainPage;
            Title = poi.DisplayName;
        }

        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
        }

        private void DriveButtonClick(object sender, RoutedEventArgs e)
        {
            BasicGeoposition target = new BasicGeoposition();
            target.Latitude = poi.Location.Position.Latitude;
            target.Longitude = poi.Location.Position.Longitude;
            MainPage.showRoute(current, target, mp, true, mp.DestinationTextBox);
            Hide();
        }

        private void WalkButtonClick(object sender, RoutedEventArgs e)
        {
            BasicGeoposition target = new BasicGeoposition();
            target.Latitude = poi.Location.Position.Latitude;
            target.Longitude = poi.Location.Position.Longitude;
            MainPage.showRoute(current, target, mp, false, mp.DestinationTextBox);
            Hide();
        }
    }
}
