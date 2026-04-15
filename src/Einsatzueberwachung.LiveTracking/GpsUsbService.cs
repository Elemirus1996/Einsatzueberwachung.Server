using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Einsatzueberwachung.LiveTracking
{
        public class GpsUsbService : IDisposable
        {
            private readonly GpsUsbDevice _gpsDevice;
            private readonly object _lock = new();
            private CancellationTokenSource? _cts;
            private bool _isProcessing;

            public bool IsProcessing
            {
                get { lock (_lock) { return _isProcessing; } }
            }

            public event Action<PvtDataD800>? MainDevicePvtUpdated;
            public event Action<DogCollarData>? DogDataUpdated;
            // public event Action<List<TrackedEntityData>>? MultiPersonDataUpdated; // If using packet 0x72
            public event Action<string>? StatusMessageChanged;
            public event Action<bool>? IsConnectedChanged;

            public GpsUsbService()
            {
                // GpsPacketProcessor is not strictly needed if GpsUsbDevice now dispatches typed data
                _gpsDevice = new GpsUsbDevice(null); // Pass null or a simplified processor

                // Configure GpsUsbDevice to call our handlers
                _gpsDevice.SetPvtDataHandler(pvtData =>
                    Application.Current.Dispatcher.Invoke(() => MainDevicePvtUpdated?.Invoke(pvtData)));

                _gpsDevice.SetDogCollarDataHandler(dogData =>
                    Application.Current.Dispatcher.Invoke(() => DogDataUpdated?.Invoke(dogData)));

                // If you had a multi-person handler for packet 0x72
                // _gpsDevice.SetMultiPersonDataHandler(entities =>
                //    Application.Current.Dispatcher.Invoke(() => MultiPersonDataUpdated?.Invoke(entities)));

                _gpsDevice.SetStatusMessageHandler(message =>
                    Application.Current.Dispatcher.Invoke(() => StatusMessageChanged?.Invoke(message)));

                _gpsDevice.SetUsbProtocolLayerDataHandler(usbHeader =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (usbHeader.PacketType == 0 && usbHeader.ApplicationPacketID == NativeMethods.PID_SESSION_STARTED)
                        {
                            StatusMessageChanged?.Invoke("USB Session Started with device. Sending Start PVT command...");
                            // This command should ideally be sent after confirming session started
                            // and device is ready. GpsUsbDevice can manage this internally.
                            _gpsDevice.SendStartPvtDataCommand();
                        }
                    });
                });
            }

            public async Task StartAsync()
            {
                CancellationToken token;
                lock (_lock)
                {
                    if (_isProcessing) return;
                    _isProcessing = true;

                    // Dispose the previous token source and create a new one atomically.
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                    token = _cts.Token;
                }

                bool wasConnected = false;
                try
                {
                    StatusMessageChanged?.Invoke("Attempting to connect to GPS device...");

                    bool connected = await Task.Run(() => _gpsDevice.Connect(), token);

                    if (connected)
                    {
                        Application.Current.Dispatcher.Invoke(() => IsConnectedChanged?.Invoke(true));
                        StatusMessageChanged?.Invoke("Device connected. Starting listener...");
                        wasConnected = true;
                        // StartListening blocks until the device disconnects or Stop() cancels it.
                        await Task.Run(() => _gpsDevice.StartListening(), token);
                    }
                    else
                    {
                        StatusMessageChanged?.Invoke("Could not connect to GPS device.");
                    }
                }
                catch (OperationCanceledException)
                {
                    StatusMessageChanged?.Invoke("GPS connection cancelled.");
                }
                catch (Exception ex)
                {
                    StatusMessageChanged?.Invoke($"GPS error: {ex.Message}");
                }
                finally
                {
                    // Always notify disconnection if we were connected, and always release the
                    // processing flag so the timer can trigger a new attempt.
                    if (wasConnected)
                    {
                        Application.Current.Dispatcher.Invoke(() => IsConnectedChanged?.Invoke(false));
                        StatusMessageChanged?.Invoke("USB listening stopped.");
                    }
                    lock (_lock) { _isProcessing = false; }
                }
            }

            public void Stop()
            {
                CancellationTokenSource? cts;
                lock (_lock)
                {
                    cts = _cts;
                }
                // Cancel the token so the Task.Run inside StartAsync finishes.
                // StartAsync's finally block will reset _isProcessing and fire IsConnectedChanged(false).
                // The CTS may have been disposed by a concurrent StartAsync initialisation;
                // in that case the old token is irrelevant (a new one has replaced it).
                try { cts?.Cancel(); }
                catch (ObjectDisposedException) { }
                _gpsDevice.StopListening();
            }

            public void Dispose()
            {
                Stop();
                _gpsDevice?.Dispose();
                _cts?.Dispose();
            }
        }
    }
