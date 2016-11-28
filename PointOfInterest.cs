﻿using System;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Newtonsoft.Json.Linq;

namespace Place2Be
{
    public class PointOfInterest
    {
        public string DisplayName { get; set; }
        public Uri ImageSourceUri { get; set; }
        public Point NormalizedAnchorPoint { get; set; }
        public Geopoint Location { get; set; }
        public JObject FullArray { get; set; }
    }
}