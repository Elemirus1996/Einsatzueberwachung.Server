using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace Einsatzueberwachung.LiveTracking.Models
{
    public partial class DogTrackInfo : ObservableObject
    {
        [ObservableProperty]
        private string _dogName = string.Empty;

        [ObservableProperty]
        private int _collarId;

        [ObservableProperty]
        private double _latitude;

        [ObservableProperty]
        private double _longitude;

        [ObservableProperty]
        private string _utmString = string.Empty;

        [ObservableProperty]
        private string _decimalLonLat = "--";

        [ObservableProperty]
        private DateTime _lastUpdate;

        [ObservableProperty]
        private int _batteryLevel;

        [ObservableProperty]
        private int _gpsStrength;

        [ObservableProperty]
        private int _commStrength;

        [ObservableProperty]
        private float _altitude;

        [ObservableProperty]
        private bool _lastSendSuccess;

        [ObservableProperty]
        private int _totalPointsSent;

        public ObservableCollection<string> RecentPositions { get; } = new();

        public void UpdateFromCollarData(DogCollarData data)
        {
            DogName = data.DogName;
            CollarId = data.ID;
            Latitude = data.LatitudeDegrees;
            Longitude = data.LongitudeDegrees;
            LastUpdate = DateTime.Now;
            BatteryLevel = data.BatteryLevel;
            GpsStrength = data.GpsStrength;
            CommStrength = data.CommStrength;
            Altitude = data.AltitudeMeters;

            try
            {
                UtmString = CoordinateTranformer.ToUtm(data.LatitudeDegrees, data.LongitudeDegrees).ToString();
            }
            catch
            {
                UtmString = $"{data.LatitudeDegrees:F6}°N, {data.LongitudeDegrees:F6}°E";
            }

            DecimalLonLat = $"{data.LatitudeDegrees:F6}, {data.LongitudeDegrees:F6}";

            var posEntry = $"{DateTime.Now:HH:mm:ss} - {UtmString}";
            RecentPositions.Insert(0, posEntry);
            if (RecentPositions.Count > 50)
                RecentPositions.RemoveAt(RecentPositions.Count - 1);
        }
    }
}
