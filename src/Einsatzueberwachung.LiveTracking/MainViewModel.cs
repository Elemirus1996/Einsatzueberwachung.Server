using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Einsatzueberwachung.LiveTracking.Models;
using Einsatzueberwachung.LiveTracking.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Einsatzueberwachung.LiveTracking
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly GpsUsbService _gpsService;
        private readonly ServerApiClient _serverApiClient;
        private readonly GpsSimulationService _simulationService;
        private readonly Dictionary<int, DogTrackInfo> _dogTrackMap = new();
        private readonly Timer _gpsCheckTimer;
        private readonly object _connectLock = new();
        private Task? _connectTask;

        [ObservableProperty]
        private string _statusMessage = "Bereit. Warte auf GPS-Gerät...";

        [ObservableProperty]
        private bool _isGpsConnected;

        [ObservableProperty]
        private bool _isServerConnected;

        [ObservableProperty]
        private string _serverUrl = string.Empty;

        [ObservableProperty]
        private PvtDataD800? _currentPvtData;

        [ObservableProperty]
        private string _currentUtmString = "N/A";

        [ObservableProperty]
        private string _mainDeviceCoords = "--";

        [ObservableProperty]
        private string _mainDeviceDecimalLonLat = "--";

        [ObservableProperty]
        private int _totalLocationsSent;

        [ObservableProperty]
        private int _totalLocationsFailed;

        [ObservableProperty]
        private bool _isSimulationActive;

        public string ServerConnectButtonText => IsServerConnected ? "Trennen" : "Verbinden";
        public string SimulationButtonText => IsSimulationActive ? "Simulation stoppen" : "Simulation starten";

        public ObservableCollection<DogTrackInfo> DogTracks { get; } = new();
        public ObservableCollection<string> LogMessages { get; } = new();

        public MainViewModel()
        {
            _serverApiClient = new ServerApiClient();
            _serverApiClient.StatusChanged += msg => AddLog(msg);

            _gpsService = new GpsUsbService();
            _gpsService.StatusMessageChanged += OnGpsStatusChanged;
            _gpsService.IsConnectedChanged += OnGpsConnectionChanged;
            _gpsService.MainDevicePvtUpdated += OnMainDevicePvtUpdated;
            _gpsService.DogDataUpdated += OnDogDataUpdated;

            _simulationService = new GpsSimulationService();
            _simulationService.StatusMessageChanged += OnGpsStatusChanged;
            _simulationService.IsConnectedChanged += OnGpsConnectionChanged;
            _simulationService.MainDevicePvtUpdated += OnMainDevicePvtUpdated;
            _simulationService.DogDataUpdated += OnDogDataUpdated;

            // Start checking for the GPS device immediately, then every 5 seconds.
            _gpsCheckTimer = new Timer(OnGpsCheckTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        private void OnGpsCheckTimerTick(object? state)
        {
            lock (_connectLock)
            {
                // Skip if a previous attempt is still in flight.
                if (_connectTask != null && !_connectTask.IsCompleted)
                    return;

                if (!IsGpsConnected && !_gpsService.IsProcessing)
                {
                    AddLog("GPS-Gerät nicht verbunden. Starte Verbindungsversuch...");
                    _connectTask = TryStartGpsAsync();
                }
            }
        }

        private async Task TryStartGpsAsync()
        {
            try
            {
                await _gpsService.StartAsync();
            }
            catch (Exception ex)
            {
                AddLog($"Unerwarteter GPS-Fehler: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ConnectServerAsync()
        {
            if (IsServerConnected)
            {
                _serverApiClient.Disconnect();
                IsServerConnected = false;
                AddLog("Server-Verbindung getrennt.");
                StatusMessage = "Server getrennt.";
                return;
            }

            if (string.IsNullOrWhiteSpace(ServerUrl))
            {
                StatusMessage = "Bitte Server-URL eingeben.";
                return;
            }

            _serverApiClient.Configure(ServerUrl);
            AddLog($"Teste Verbindung zu {ServerUrl}...");

            bool connected = await _serverApiClient.TestConnectionAsync();
            IsServerConnected = connected;

            if (connected)
            {
                AddLog("Server-Verbindung erfolgreich.");
                StatusMessage = "Server verbunden.";
                Properties.Settings.Default.ServerUrl = ServerUrl;
                Properties.Settings.Default.Save();
            }
            else
            {
                AddLog("Server nicht erreichbar.");
                StatusMessage = "Server-Verbindung fehlgeschlagen.";
            }
        }

        partial void OnIsServerConnectedChanged(bool value)
        {
            OnPropertyChanged(nameof(ServerConnectButtonText));
        }

        partial void OnIsSimulationActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(SimulationButtonText));
        }

        [RelayCommand]
        private void ToggleSimulation()
        {
            if (IsSimulationActive)
            {
                // Stop simulation, re-enable real device auto-connect
                _simulationService.Stop();
                IsSimulationActive = false;
                _gpsCheckTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                AddLog("Simulation beendet. Automatische Gerätesuche wieder aktiv.");
            }
            else
            {
                // Stop real device attempts, start simulation
                _gpsCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _gpsService.Stop();
                _simulationService.Start();
                IsSimulationActive = true;
                AddLog("Simulationsmodus aktiviert.");
            }
        }

        public void LoadSettings()
        {
            ServerUrl = Properties.Settings.Default.ServerUrl ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(ServerUrl))
            {
                _serverApiClient.Configure(ServerUrl);
            }
        }

        private void OnGpsStatusChanged(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = message;
            });
        }

        private void OnGpsConnectionChanged(bool connected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsGpsConnected = connected;
                AddLog(connected ? "GPS-Gerät verbunden." : "GPS-Gerät getrennt.");
            });
        }

        private void OnMainDevicePvtUpdated(PvtDataD800 pvtData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentPvtData = pvtData;
                MainDeviceCoords = $"{pvtData.LatitudeDegrees:F5}°N, {pvtData.LongitudeDegrees:F5}°E";
                MainDeviceDecimalLonLat = $"{pvtData.LatitudeDegrees:F6}, {pvtData.LongitudeDegrees:F6}";

                if (pvtData.FixType >= 2 && pvtData.LatitudeDegrees >= -80 && pvtData.LatitudeDegrees <= 84)
                {
                    try
                    {
                        CurrentUtmString = CoordinateTranformer.ToUtm(pvtData.LongitudeDegrees, pvtData.LatitudeDegrees).ToString();
                    }
                    catch
                    {
                        CurrentUtmString = "UTM Fehler";
                    }
                }
                else
                {
                    CurrentUtmString = "Kein GPS-Fix";
                }
            });
        }

        private async void OnDogDataUpdated(DogCollarData dogData)
        {
            DogTrackInfo? trackInfo = null;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(dogData.DogName)) return;

                if (!_dogTrackMap.TryGetValue(dogData.ID, out trackInfo))
                {
                    trackInfo = new DogTrackInfo();
                    _dogTrackMap[dogData.ID] = trackInfo;
                    DogTracks.Add(trackInfo);
                    AddLog($"Neuer Hund erkannt: {dogData.DogName} (ID: {dogData.ID})");
                }

                trackInfo.UpdateFromCollarData(dogData);
            });

            if (trackInfo != null && IsServerConnected)
            {
                bool success = await _serverApiClient.SendCollarLocationAsync(
                    dogData.ID.ToString(),
                    dogData.DogName,
                    dogData.LatitudeDegrees,
                    dogData.LongitudeDegrees);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (trackInfo != null)
                    {
                        trackInfo.LastSendSuccess = success;
                        if (success)
                        {
                            trackInfo.TotalPointsSent++;
                            TotalLocationsSent++;
                        }
                        else
                        {
                            TotalLocationsFailed++;
                        }
                    }
                });
            }
        }

        private void AddLog(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                LogMessages.Insert(0, entry);
                if (LogMessages.Count > 200)
                    LogMessages.RemoveAt(LogMessages.Count - 1);
            }
            else
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    LogMessages.Insert(0, entry);
                    if (LogMessages.Count > 200)
                        LogMessages.RemoveAt(LogMessages.Count - 1);
                });
            }
        }

        public void Dispose()
        {
            // Wait for any in-flight timer callback to finish before disposing
            // the services it depends on.
            using (var timerDone = new ManualResetEvent(false))
            {
                _gpsCheckTimer.Dispose(timerDone);
                timerDone.WaitOne();
            }
            _simulationService.Dispose();
            _gpsService.Dispose();
            _serverApiClient.Dispose();
        }
    }
}

