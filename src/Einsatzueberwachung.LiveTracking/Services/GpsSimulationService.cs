using System;
using System.Text;
using System.Threading;

namespace Einsatzueberwachung.LiveTracking.Services
{
    /// <summary>
    /// Generates simulated USB-style binary payloads for 3 dog collars and a main device,
    /// parsing them through the same FromPayload methods as real hardware to test the full pipeline.
    /// </summary>
    public class GpsSimulationService : IDisposable
    {
        private Timer? _timer;
        private bool _disposed;
        private readonly Random _rng = new();

        // Stadtpark Gelsenkirchen center
        private const double CenterLat = 51.505592;
        private const double CenterLon = 7.085900;
        private const float CenterAlt = 58.0f;

        // Max distance from center in degrees (~200m)
        private const double MaxOffsetLat = 0.0018;
        private const double MaxOffsetLon = 0.0029;

        // Step size per tick in degrees (~2-5m per second)
        private const double StepLat = 0.000025;
        private const double StepLon = 0.000040;

        private readonly SimDogState[] _dogs =
        {
            new(901, "Simulation 1"),
            new(902, "Simulation 2"),
            new(903, "Simulation 3"),
        };

        public event Action<PvtDataD800>? MainDevicePvtUpdated;
        public event Action<DogCollarData>? DogDataUpdated;
        public event Action<string>? StatusMessageChanged;
        public event Action<bool>? IsConnectedChanged;

        public bool IsRunning { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;

            // Scatter dogs at random starting positions near center
            foreach (var dog in _dogs)
            {
                dog.Lat = CenterLat + (_rng.NextDouble() - 0.5) * 2 * MaxOffsetLat * 0.5;
                dog.Lon = CenterLon + (_rng.NextDouble() - 0.5) * 2 * MaxOffsetLon * 0.5;
                dog.Heading = _rng.NextDouble() * 2 * Math.PI;
            }

            StatusMessageChanged?.Invoke("Simulation gestartet: 3 Hundehalsbänder um Stadtpark Gelsenkirchen");
            IsConnectedChanged?.Invoke(true);
            _timer = new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _timer?.Dispose();
            _timer = null;
            IsRunning = false;
            StatusMessageChanged?.Invoke("Simulation gestoppt.");
            IsConnectedChanged?.Invoke(false);
        }

        private void OnTick(object? state)
        {
            // --- Main device PVT (stationary at park center) ---
            try
            {
                byte[] pvtPayload = BuildPvtPayload(CenterLat, CenterLon, CenterAlt);
                var pvt = PvtDataD800.FromPayload(pvtPayload);
                MainDevicePvtUpdated?.Invoke(pvt);
            }
            catch (Exception ex)
            {
                StatusMessageChanged?.Invoke($"Sim PVT Fehler: {ex.Message}");
            }

            // --- Dog collar data (random walk) ---
            foreach (var dog in _dogs)
            {
                try
                {
                    // Slightly adjust heading each tick (smooth turning, ±30°)
                    dog.Heading += (_rng.NextDouble() - 0.5) * Math.PI / 3.0;

                    double newLat = dog.Lat + StepLat * Math.Cos(dog.Heading);
                    double newLon = dog.Lon + StepLon * Math.Sin(dog.Heading);

                    // Soft boundary: if wandering too far, steer back toward center
                    double dLat = newLat - CenterLat;
                    double dLon = newLon - CenterLon;
                    if (Math.Abs(dLat) > MaxOffsetLat || Math.Abs(dLon) > MaxOffsetLon)
                    {
                        dog.Heading = Math.Atan2(CenterLon - dog.Lon, CenterLat - dog.Lat)
                                      + (_rng.NextDouble() - 0.5) * Math.PI / 4.0;
                        newLat = dog.Lat + StepLat * Math.Cos(dog.Heading);
                        newLon = dog.Lon + StepLon * Math.Sin(dog.Heading);
                    }

                    dog.Lat = newLat;
                    dog.Lon = newLon;
                    float alt = CenterAlt + (float)(_rng.NextDouble() - 0.5) * 4.0f;

                    byte[] collarPayload = BuildDogCollarPayload(dog.Id, dog.Name, dog.Lat, dog.Lon, alt);
                    var collarData = DogCollarData.FromPayload(collarPayload);
                    if (collarData != null)
                    {
                        DogDataUpdated?.Invoke(collarData);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessageChanged?.Invoke($"Sim Halsband Fehler ({dog.Name}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Builds a 100-byte binary payload matching the real dog collar USB packet format (AppID 0x0C06).
        /// </summary>
        private static byte[] BuildDogCollarPayload(short id, string name, double lat, double lon, float altitude)
        {
            var payload = new byte[100];

            // Offset 0-3: Latitude (semicircles)
            BitConverter.GetBytes(DegreesToSemicircles(lat)).CopyTo(payload, 0);
            // Offset 4-7: Longitude (semicircles)
            BitConverter.GetBytes(DegreesToSemicircles(lon)).CopyTo(payload, 4);
            // Offset 8-11: Timestamp (seconds since GPS epoch)
            uint gpsTimestamp = (uint)(DateTime.UtcNow - GpsDataConverter.GpsEpoch).TotalSeconds;
            BitConverter.GetBytes(gpsTimestamp).CopyTo(payload, 8);
            // Offset 12-15: Altitude (float)
            BitConverter.GetBytes(altitude).CopyTo(payload, 12);
            // Offset 16-19: RawStatusA — battery=3, comm=3, gps=3 (all max)
            //   LSB: gps(2bits)<<4 | comm(2bits)<<2 | batt(2bits) = 0b00_11_11_11 = 0x3F
            BitConverter.GetBytes((uint)0x0000003F).CopyTo(payload, 16);
            // Offset 20-23: RawStatusB — ID lives at bytes 22-23
            payload[20] = 0x01; // StatusByte20
            payload[21] = 0x00; // ColorCandidateByte21
            BitConverter.GetBytes(id).CopyTo(payload, 22); // ID at byte 22-23
            // Offset 24-25: RawStatusC
            BitConverter.GetBytes((ushort)0).CopyTo(payload, 24);
            // Offset 26-30: reserved (zeros)
            // Offset 31+: Dog name (null-terminated ASCII)
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            int maxNameLen = 100 - 31 - 15; // leave room for null + fixed tail
            int nameLen = Math.Min(nameBytes.Length, maxNameLen);
            Array.Copy(nameBytes, 0, payload, 31, nameLen);
            payload[31 + nameLen] = 0; // null terminator
            // Remaining bytes (dynamic block + fixed tail) stay zero

            return payload;
        }

        /// <summary>
        /// Builds a 64-byte binary payload matching the real PVT D800 USB packet format (AppID 0x33).
        /// </summary>
        private static byte[] BuildPvtPayload(double lat, double lon, float altitude)
        {
            var payload = new byte[64];
            int o = 0;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            // Compute GPS week time components
            var elapsed = DateTime.UtcNow - GpsDataConverter.GpsEpoch;
            uint weekNumberDays = (uint)(Math.Floor(elapsed.TotalDays / 7.0) * 7);
            double tow = (elapsed.TotalDays - weekNumberDays) * 86400.0;
            const short leapSeconds = 18;

            BitConverter.GetBytes(altitude).CopyTo(payload, o); o += 4;          // Altitude
            BitConverter.GetBytes(5.0f).CopyTo(payload, o); o += 4;              // EPE
            BitConverter.GetBytes(3.0f).CopyTo(payload, o); o += 4;              // EPH
            BitConverter.GetBytes(4.0f).CopyTo(payload, o); o += 4;              // EPV
            BitConverter.GetBytes((ushort)3).CopyTo(payload, o); o += 2;          // FixType = 3D
            BitConverter.GetBytes(tow).CopyTo(payload, o); o += 8;               // TimeOfWeek
            BitConverter.GetBytes(latRad).CopyTo(payload, o); o += 8;             // Latitude (radians)
            BitConverter.GetBytes(lonRad).CopyTo(payload, o); o += 8;             // Longitude (radians)
            BitConverter.GetBytes(0.0f).CopyTo(payload, o); o += 4;              // VelocityEast
            BitConverter.GetBytes(0.0f).CopyTo(payload, o); o += 4;              // VelocityNorth
            BitConverter.GetBytes(0.0f).CopyTo(payload, o); o += 4;              // VelocityUp
            BitConverter.GetBytes(altitude).CopyTo(payload, o); o += 4;          // MslHeight
            BitConverter.GetBytes(leapSeconds).CopyTo(payload, o); o += 2;        // LeapSeconds
            BitConverter.GetBytes(weekNumberDays).CopyTo(payload, o); o += 4;     // WeekNumberDays

            return payload;
        }

        private static int DegreesToSemicircles(double degrees)
        {
            return (int)(degrees * (Math.Pow(2, 31) / 180.0));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }

        private sealed class SimDogState
        {
            public short Id { get; }
            public string Name { get; }
            public double Lat { get; set; }
            public double Lon { get; set; }
            public double Heading { get; set; }

            public SimDogState(short id, string name)
            {
                Id = id;
                Name = name;
            }
        }
    }
}
