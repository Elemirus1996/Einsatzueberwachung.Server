using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Einsatzueberwachung.LiveTracking
{
    public static class GpsDataConverter
    {
        public static readonly DateTime GpsEpoch = new DateTime(1989, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        public static double SemicirclesToDegrees(int semicircles)
        {
            return semicircles * (180.0 / Math.Pow(2, 31));
        }

        public static double RadiansToDegrees(double radians)
        {
            return radians * (180.0 / Math.PI);
        }

        public static byte[] HexStringToByteArray(string hex)
        {
            hex = hex.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have an even number of digits.");

            byte[] arr = new byte[hex.Length >> 1];
            for (int i = 0; i < hex.Length >> 1; ++i)
            {
                arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
            }
            return arr;
        }

        public static int GetHexVal(char hex)
        {
            int val = (int)hex;
            // For lower case a-f letters.
            if (val >= 'a' && val <= 'f') return val - 'a' + 10;
            // For upper case A-F letters.
            if (val >= 'A' && val <= 'F') return val - 'A' + 10;
            // For digits.
            if (val >= '0' && val <= '9') return val - '0';
            throw new ArgumentException("Invalid hex character.");
        }
    }

    public struct UtmCoordinate
    {
        public double Easting;
        public double Northing;
        public int ZoneNumber;
        public char ZoneLetter;

        public override string ToString()
        {
            return $"{ZoneNumber}{ZoneLetter} {Easting:F0} {Northing:F0}";
        }
    }

    public static class CoordinateTranformer
    {
        private const double WGS84_A = 6378137.0; // Semi-major axis
        private const double WGS84_ECC_SQ = 0.00669438002290; // Eccentricity squared
        private const double UTM_K0 = 0.9996; // UTM scale factor on central meridian

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static char GetUtmZoneLetter(double latitude)
        {
            if (latitude < -80.0 || latitude > 84.0) return ' ';
            char[] letters = "CDEFGHJKLMNPQRSTUVWX".ToCharArray();
            int index = (int)Math.Floor((latitude + 80.0) / 8.0);
            if (index < 0) index = 0;
            if (index >= letters.Length) index = letters.Length - 1;
            return letters[index];
        }

        public static UtmCoordinate ToUtm(double longitudeDegrees, double latitudeDegrees)
        {
            if (latitudeDegrees < -80.0 || latitudeDegrees > 84.0)
            {
                throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), "Latitude out of UTM range (-80 to 84).");
            }

            double falseEasting = 500000.0;
            double falseNorthing = (latitudeDegrees < 0) ? 10000000.0 : 0.0;

            double latRad = ToRadians(latitudeDegrees);

            int zoneNumber = (int)Math.Floor((longitudeDegrees + 180.0) / 6.0) + 1;
            double centralMeridianRad = ToRadians((zoneNumber - 1) * 6.0 - 180.0 + 3.0);

            double eccPrimeSq = WGS84_ECC_SQ / (1.0 - WGS84_ECC_SQ);

            double N = WGS84_A / Math.Sqrt(1.0 - WGS84_ECC_SQ * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = eccPrimeSq * Math.Cos(latRad) * Math.Cos(latRad);
            double A = (ToRadians(longitudeDegrees) - centralMeridianRad) * Math.Cos(latRad);

            double M = WGS84_A * (
                (1.0 - WGS84_ECC_SQ / 4.0 - 3.0 * Math.Pow(WGS84_ECC_SQ, 2) / 64.0 - 5.0 * Math.Pow(WGS84_ECC_SQ, 3) / 256.0) * latRad -
                (3.0 * WGS84_ECC_SQ / 8.0 + 3.0 * Math.Pow(WGS84_ECC_SQ, 2) / 32.0 + 45.0 * Math.Pow(WGS84_ECC_SQ, 3) / 1024.0) * Math.Sin(2.0 * latRad) +
                (15.0 * Math.Pow(WGS84_ECC_SQ, 2) / 256.0 + 45.0 * Math.Pow(WGS84_ECC_SQ, 3) / 1024.0) * Math.Sin(4.0 * latRad) -
                (35.0 * Math.Pow(WGS84_ECC_SQ, 3) / 3072.0) * Math.Sin(6.0 * latRad)
            );

            double easting = UTM_K0 * N * (
                A + (1.0 - T + C) * Math.Pow(A, 3) / 6.0 +
                (5.0 - 18.0 * T + T * T + 72.0 * C - 58.0 * eccPrimeSq) * Math.Pow(A, 5) / 120.0
            ) + falseEasting;

            double northing = UTM_K0 * (
                M + N * Math.Tan(latRad) * (
                    A * A / 2.0 +
                    (5.0 - T + 9.0 * C + 4.0 * C * C) * Math.Pow(A, 4) / 24.0 +
                    (61.0 - 58.0 * T + T * T + 600.0 * C - 330.0 * eccPrimeSq) * Math.Pow(A, 6) / 720.0
                )
            ) + falseNorthing;

            return new UtmCoordinate
            {
                Easting = easting,
                Northing = northing,
                ZoneNumber = zoneNumber,
                ZoneLetter = GetUtmZoneLetter(latitudeDegrees)
            };
        }
    }

    public class GpsUsbPacketHeader
    {
        public byte PacketType { get; set; }
        public ushort ApplicationPacketID { get; set; }
        public uint PayloadDataSize { get; set; }

        public static GpsUsbPacketHeader? FromBytes(byte[] packetBytes, int offset = 0)
        {
            if (packetBytes == null || packetBytes.Length < offset + 12) return null;
            return new GpsUsbPacketHeader
            {
                PacketType = packetBytes[offset + 0],
                ApplicationPacketID = BitConverter.ToUInt16(packetBytes, offset + 4),
                PayloadDataSize = BitConverter.ToUInt32(packetBytes, offset + 8)
            };
        }
        public override string ToString() => $"USB Hdr: Type=0x{PacketType:X2}, AppID=0x{ApplicationPacketID:X4}({ApplicationPacketID}), PayloadSize={PayloadDataSize}B";
    }

    public class TrackedEntityData // For Packet ID 0x72
    {
        public double LongitudeDegrees { get; private set; }
        public double LatitudeDegrees { get; private set; }
        public uint IdentifierStatus { get; private set; }

        public static TrackedEntityData? FromBytes(byte[] payload, int offset)
        {
            if (payload.Length < offset + 12) return null;
            return new TrackedEntityData
            {
                LongitudeDegrees = GpsDataConverter.SemicirclesToDegrees(BitConverter.ToInt32(payload, offset)),
                LatitudeDegrees = GpsDataConverter.SemicirclesToDegrees(BitConverter.ToInt32(payload, offset + 4)),
                IdentifierStatus = BitConverter.ToUInt32(payload, offset + 8)
            };
        }
        public override string ToString()
        {
            string baseStr = $"Tracked Entity: ID/Status=0x{IdentifierStatus:X8}, Lon={LongitudeDegrees:F6}�, Lat={LatitudeDegrees:F6}�";
            try
            {
                UtmCoordinate utm = CoordinateTranformer.ToUtm(LatitudeDegrees, LongitudeDegrees);
                return $"{baseStr}\n    UTM: {utm}";
            }
            catch { return $"{baseStr}\n    UTM: (Conversion Error/Out of Range)"; }
        }
    }

    public class PvtDataD800
    {
        public float Altitude { get; private set; }
        public float EPE { get; private set; } // Estimated Position Error (2-sigma)
        public float EPH { get; private set; } // Estimated Horizontal Position Error
        public float EPV { get; private set; } // Estimated Vertical Position Error
        public ushort FixType { get; private set; }
        public double TimeOfWeek { get; private set; } // GPS Time Of Week in seconds
        public double LatitudeRadians { get; private set; }
        public double LongitudeRadians { get; private set; }
        public float VelocityEast { get; private set; } // m/s
        public float VelocityNorth { get; private set; } // m/s
        public float VelocityUp { get; private set; } // m/s
        public float MslHeight { get; private set; } // WGS84 ellipsoid height above MSL (meters)
        public short LeapSeconds { get; private set; } // GPS - UTC difference
        public uint WeekNumberDays { get; private set; } // Days from GpsEpoch (1989-12-31) to start of current GPS week

        // Calculated properties
        public double LatitudeDegrees => GpsDataConverter.RadiansToDegrees(LatitudeRadians);
        public double LongitudeDegrees => GpsDataConverter.RadiansToDegrees(LongitudeRadians);

        public DateTime CalculatedUtcTime
        {
            get
            {
                try
                {
                    // A very basic check for uninitialized data, can be made more robust
                    if (WeekNumberDays == 0 && Math.Abs(TimeOfWeek) < 1.0 && LeapSeconds == 0 && FixType == 0)
                    {
                        return DateTime.MinValue; // Indicates likely invalid/uninitialized PVT data
                    }
                    // TOW is seconds into the GPS week. wn_days is days from GpsEpoch to start of that week.
                    // LeapSeconds adjusts GPS time towards UTC. GPS time = TOW + (WeekNum * 7 * 24 * 3600)
                    // UTC time = GPS time - LeapSeconds
                    TimeSpan timeIntoWeek = TimeSpan.FromSeconds(TimeOfWeek);
                    DateTime weekStartDate = GpsDataConverter.GpsEpoch.AddDays(WeekNumberDays);
                    DateTime gpsTime = weekStartDate + timeIntoWeek;
                    return gpsTime.AddSeconds(-LeapSeconds);
                }
                catch
                {
                    return DateTime.MinValue; // Error in calculation
                }
            }
        }

        private string GetFixTypeString() // Renamed from GetFixTypeString(ushort fixVal)
        {
            // Based on D800_Pvt_Data_Type fix enum (PDF page 51)
            // Note: PDF also mentions legacy devices add 1 to these values.
            // This parser assumes non-legacy values.
            switch (this.FixType)
            {
                case 0: return "Unusable (integrity fail)";
                case 1: return "Invalid / Unavailable";
                case 2: return "2D";
                case 3: return "3D";
                case 4: return "2D Differential";
                case 5: return "3D Differential";
                default: return $"Unknown ({this.FixType})";
            }
        }

        public static PvtDataD800 FromPayload(byte[] payload, int initialOffset = 0)
        {
            // D800_Pvt_Data_Type is 64 bytes long.
            if (payload.Length < initialOffset + 64)
            {
                // Only throw if we genuinely don't have enough bytes for the core D800 structure.
                // If payload.Length is, for example, 62 (due to truncation in capture but header said 64),
                // this would prevent parsing. The caller (GpsPacketProcessor) should already ensure
                // that 'payload' passed here has the 'declaredPayloadSize' if available from the full packet.
                // Let's assume 'payload' here IS the GPS payload of 'declaredPayloadSize' (e.g. 64 bytes).
                throw new ArgumentException($"Payload too short for D800 PVT data. Expected at least 64 bytes from offset {initialOffset}, got {payload.Length - initialOffset}.");
            }

            var pvt = new PvtDataD800();
            int o = initialOffset; // Use 'o' as the running offset from the start of where D800 data begins in payload.

            pvt.Altitude = BitConverter.ToSingle(payload, o); o += 4;
            pvt.EPE = BitConverter.ToSingle(payload, o); o += 4;
            pvt.EPH = BitConverter.ToSingle(payload, o); o += 4;
            pvt.EPV = BitConverter.ToSingle(payload, o); o += 4;
            pvt.FixType = BitConverter.ToUInt16(payload, o); o += 2;
            // Assuming tow starts at byte 18 of the D800 structure (alt(4)+epe(4)+eph(4)+epv(4)+fix(2) = 18)
            pvt.TimeOfWeek = BitConverter.ToDouble(payload, o); o += 8;       // o is now initialOffset + 18 + 8 = initialOffset + 26
            pvt.LatitudeRadians = BitConverter.ToDouble(payload, o); o += 8;  // o is now initialOffset + 26 + 8 = initialOffset + 34
            pvt.LongitudeRadians = BitConverter.ToDouble(payload, o); o += 8; // o is now initialOffset + 34 + 8 = initialOffset + 42
            pvt.VelocityEast = BitConverter.ToSingle(payload, o); o += 4;     // o is now initialOffset + 42 + 4 = initialOffset + 46
            pvt.VelocityNorth = BitConverter.ToSingle(payload, o); o += 4;    // o is now initialOffset + 46 + 4 = initialOffset + 50
            pvt.VelocityUp = BitConverter.ToSingle(payload, o); o += 4;       // o is now initialOffset + 50 + 4 = initialOffset + 54
            pvt.MslHeight = BitConverter.ToSingle(payload, o); o += 4;      // o is now initialOffset + 54 + 4 = initialOffset + 58
            pvt.LeapSeconds = BitConverter.ToInt16(payload, o); o += 2;     // o is now initialOffset + 58 + 2 = initialOffset + 60
            pvt.WeekNumberDays = BitConverter.ToUInt32(payload, o); o += 4;  // o is now initialOffset + 60 + 4 = initialOffset + 64

            // The D800 structure is 64 bytes. If the payload was exactly 64 bytes, 'o' is now at the end.
            // No "AdditionalPayload" is parsed from within D800 itself.
            // If the GPS payload (as declared by USB header) was > 64 bytes,
            // the caller would handle those extra bytes.

            return pvt;
        }

        public override string ToString()
        {
            string utmString = "UTM: (Unavailable or out of range)";
            if (LatitudeDegrees >= -80 && LatitudeDegrees <= 84) // Check if valid for UTM
            {
                try
                {
                    UtmCoordinate utm = CoordinateTranformer.ToUtm(LatitudeDegrees, LongitudeDegrees);
                    utmString = utm.ToString();
                }
                catch (Exception ex)
                {
                    utmString = $"(UTM Error: {ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0]})";
                }
            }

            return $"PVT (Main Device - D800 based):\n" +
                   $"  Lat/Lon: {LatitudeDegrees:F6}�, {LongitudeDegrees:F6}�\n" +
                   $"UTM: {utmString}\n";
        }
    }

    public class DogCollarData // For Packet ID 0x0C06
    {
        public int ID { get; private set; }
        public double LatitudeDegrees { get; private set; }
        public double LongitudeDegrees { get; private set; }
        public DateTime TimestampUtc { get; private set; }
        public float AltitudeMeters { get; private set; }
        public uint RawStatusA { get; private set; }
        public int BatteryLevel { get; private set; }
        public int CommStrength { get; private set; }
        public int GpsStrength { get; private set; }
        public uint RawStatusB { get; private set; }
        public int ChannelPrimary { get; private set; }
        public int ChannelSecondary { get; private set; }
        public int ColorCandidateByte21 { get; private set; }
        public byte StatusByte20 { get; private set; }
        public ushort RawStatusC { get; private set; }
        public string DogName { get; private set; } = "";
        public byte[] PostNameDynamicBlock { get; private set; } = Array.Empty<byte>();
        public uint UnknownK { get; private set; }
        public uint UnknownL { get; private set; }
        public uint DogActionState { get; private set; }
        public ushort FinalTwoBytes { get; private set; }

        private DogCollarData() { }
        public static DogCollarData? FromPayload(byte[] payload)
        {
            if (payload == null || payload.Length < 26 + 1) { Console.WriteLine("DogCollarData payload too short."); return null; }
            int actualPayloadLength = Math.Min(payload.Length, 100);
            var data = new DogCollarData(); int offset = 0;
            try
            {
                data.LatitudeDegrees = GpsDataConverter.SemicirclesToDegrees(BitConverter.ToInt32(payload, offset)); offset += 4;
                data.LongitudeDegrees = GpsDataConverter.SemicirclesToDegrees(BitConverter.ToInt32(payload, offset)); offset += 4;
                uint gpsTime = BitConverter.ToUInt32(payload, offset); offset += 4;
                data.TimestampUtc = GpsDataConverter.GpsEpoch.AddSeconds(gpsTime);
                data.AltitudeMeters = BitConverter.ToSingle(payload, offset); offset += 4; // 16
                data.RawStatusA = BitConverter.ToUInt32(payload, offset); offset += 4; // 20
                byte statusA_LSB = (byte)(data.RawStatusA & 0xFF);
                data.BatteryLevel = statusA_LSB & 0x03; data.CommStrength = (statusA_LSB >> 2) & 0x03; data.GpsStrength = (statusA_LSB >> 4) & 0x03;
                data.RawStatusB = BitConverter.ToUInt32(payload, offset); offset += 4; // 24
                data.StatusByte20 = payload[offset - 4]; data.ColorCandidateByte21 = payload[offset - 3]; data.ChannelSecondary = payload[offset - 2]; data.ChannelPrimary = payload[offset - 1];
                data.RawStatusC = BitConverter.ToUInt16(payload, offset); offset += 2; // 26
                data.ID = BitConverter.ToInt16(payload, 22);

                int nameStartOffset = 31;
                int nameEndOffset = Array.IndexOf(payload, (byte)0, nameStartOffset); ;
                data.DogName = Encoding.ASCII.GetString(payload, nameStartOffset, nameEndOffset - nameStartOffset).TrimEnd((char)0);
                offset = nameEndOffset + 1;
                int fixedTailStartOffset = actualPayloadLength - 14; // K(4)+L(4)+Action(4)+Final(2) = 14 bytes
                if (fixedTailStartOffset < offset) fixedTailStartOffset = offset; // Handle cases where name is extremely long

                int dynamicBlockLength = fixedTailStartOffset - offset;
                if (dynamicBlockLength > 0)
                {
                    data.PostNameDynamicBlock = new byte[dynamicBlockLength];
                    Array.Copy(payload, offset, data.PostNameDynamicBlock, 0, dynamicBlockLength);
                }
                else { data.PostNameDynamicBlock = Array.Empty<byte>(); }
                offset = fixedTailStartOffset; // Jump to fixed tail
                if (actualPayloadLength >= offset + 4) data.UnknownK = BitConverter.ToUInt32(payload, offset); offset += 4;
                if (actualPayloadLength >= offset + 4) data.UnknownL = BitConverter.ToUInt32(payload, offset); offset += 4;
                if (actualPayloadLength >= offset + 4) data.DogActionState = BitConverter.ToUInt32(payload, offset); offset += 4;
                if (actualPayloadLength >= offset + 2) data.FinalTwoBytes = BitConverter.ToUInt16(payload, offset);
                return data;
            }
            catch (Exception ex) { Console.WriteLine($"Error parsing DogCollarData: {ex.Message} at offset {offset}"); return null; }
        }
        public string GetFormattedString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Dog Name: \"{DogName}\"");
            sb.AppendLine($"  Location: {LatitudeDegrees:F6} N, {LongitudeDegrees:F6} E");
            try { UtmCoordinate utm = CoordinateTranformer.ToUtm(LongitudeDegrees, LatitudeDegrees); sb.AppendLine($"  UTM: {utm}"); } catch { sb.AppendLine("  UTM: (Conversion Error/OOB)"); }
            //sb.AppendLine($"  StatusA (0x{RawStatusA:X8}): Batt:{BatteryLevel}, Comm:{CommStrength}, GPS:{GpsStrength}");
            //sb.AppendLine($"  Timestamp: {TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC | Altitude: {AltitudeMeters:F2} m");
            //sb.AppendLine($"  StatusB (0x{RawStatusB:X8}): ChPri:{ChannelPrimary}, ChSec:{ChannelSecondary}, Color?:{ColorCandidateByte21}, SB20:0x{StatusByte20:X2}");
            //sb.AppendLine($"  StatusC (0x{RawStatusC:X4}): {RawStatusC} (Speed/DistFlag?)");
            //sb.AppendLine($"  Action: {DogActionState} (0=Stand,1=Move) | K:0x{UnknownK:X8} L:0x{UnknownL:X8} End:0x{FinalTwoBytes:X4}");
            //if (PostNameDynamicBlock?.Length > 0) sb.AppendLine($"  PostNameBlock ({PostNameDynamicBlock.Length}B): {BitConverter.ToString(PostNameDynamicBlock).Replace("-", "")}");
            return sb.ToString();
        }
    }

    public class GpsPacketProcessor
    { /* ... as before, update case 0x0C06 ... */
        public void ProcessPacket(byte[] fullUsbPacket)
        {
            var usbHeader = GpsUsbPacketHeader.FromBytes(fullUsbPacket);
            if (usbHeader == null) { Console.WriteLine("Invalid USB packet header."); return; }
            Console.WriteLine(usbHeader);
            int actualPayloadAvailable = fullUsbPacket.Length - 12;
            int declaredPayloadSize = (int)usbHeader.PayloadDataSize;
            int payloadToProcessLength = Math.Min(actualPayloadAvailable, declaredPayloadSize);
            byte[] payload = new byte[payloadToProcessLength];
            if (payloadToProcessLength > 0) Array.Copy(fullUsbPacket, 12, payload, 0, payloadToProcessLength);
            if (actualPayloadAvailable < declaredPayloadSize) Console.WriteLine($"Warning: Truncated. Declared: {declaredPayloadSize}, Available: {actualPayloadAvailable}. Processing {payloadToProcessLength}B.");

            if (usbHeader.PacketType == 0x14)
            {
                switch (usbHeader.ApplicationPacketID)
                {
                    case 0x0C06: // Dog Collar
                        Console.WriteLine("Processing Packet ID 0x0C06 (Dog Collar Data):");
                        if (declaredPayloadSize == 100)
                        {
                            var dogData = DogCollarData.FromPayload(payload);
                            if (dogData != null) Console.WriteLine(dogData.GetFormattedString());
                        }
                        else Console.WriteLine($"  Payload issue for 0x0C06. Actual: {payloadToProcessLength}, Declared: {declaredPayloadSize}, Expected: 100.");
                        break;
                    case 0x0072: /* ... */
                        Console.WriteLine("Processing Packet ID 0x72 (Multi-Person Data):");
                        if (payloadToProcessLength >= 72 && declaredPayloadSize == 72)
                        {
                            int recordSize = 12; int numRecords = 72 / recordSize;
                            for (int i = 0; i < numRecords; i++)
                            {
                                var entityData = TrackedEntityData.FromBytes(payload, i * recordSize);
                                if (entityData != null) Console.WriteLine($"  Entity {i + 1}: {entityData}");
                            }
                        }
                        else Console.WriteLine($"  Payload issue for 0x72. Actual: {payloadToProcessLength}, Declared: {declaredPayloadSize}, Expected: 72.");
                        break;
                    case 0x0033: /* ... */
                        Console.WriteLine("Processing Packet ID 0x33 (PVT Data - Main Device):");
                        if (payloadToProcessLength >= 60 && declaredPayloadSize == 64)
                        {
                            try
                            {
                                var pvtData = PvtDataD800.FromPayload(payload);
                                Console.WriteLine(pvtData);
                                if (payloadToProcessLength > 60) Console.WriteLine($"  (Plus {payloadToProcessLength - 60} extra/padding bytes in payload at end)");
                            }
                            catch (Exception ex) { Console.WriteLine($"  Error parsing PVT data: {ex.Message}"); }
                        }
                        else Console.WriteLine($"  Payload issue for 0x33. Actual: {payloadToProcessLength}, Declared: {declaredPayloadSize}, Expected: 64 (D800=60).");
                        break;
                    default: Console.WriteLine($"  Unknown AppID: 0x{usbHeader.ApplicationPacketID:X4}"); break;
                }
            }
            else
            {
                Console.WriteLine($"  Unhandled USB Packet Type: 0x{usbHeader.PacketType:X2}");
                if (usbHeader.PacketType == 0) Console.WriteLine($"    USB Protocol PID: {usbHeader.ApplicationPacketID}");
            }
            Console.WriteLine(new string('-', 40));
        }
    }

    internal static class NativeMethods
    { /* ... as before, with corrected GUID ... */
        public static readonly Guid GUID_DEVINTERFACE_GRMNUSB = new Guid("2C9C45C2-8E7D-4C08-A12D-816BBAE722C0"); // Corrected
        public const uint DIGCF_PRESENT = 0x00000002; public const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        public const uint GENERIC_READ = 0x80000000; public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001; public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3; public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        public const uint FILE_DEVICE_UNKNOWN = 0x00000022; public const uint METHOD_BUFFERED = 0; public const uint FILE_ANY_ACCESS = 0;
        public const uint ERROR_IO_PENDING = 997; public const uint WAIT_TIMEOUT = 0x102;
        public static uint CTL_CODE(uint dT, uint f, uint m, uint a) => ((dT) << 16) | ((a) << 14) | ((f) << 2) | (m);
        public static readonly uint IOCTL_ASYNC_IN_CTL_CODE = CTL_CODE(FILE_DEVICE_UNKNOWN, 0x850, METHOD_BUFFERED, FILE_ANY_ACCESS);
        public const int ASYNC_DATA_SIZE = 64;
        public const ushort PID_DATA_AVAILABLE = 2; public const ushort PID_START_SESSION = 5; public const ushort PID_SESSION_STARTED = 6;
        public const ushort PID_COMMAND_DATA = 10; public const ushort CMD_START_PVT_DATA = 49;

        public const ushort PID_PVT_DATA = 51; //  <<<<<<<<<< CORRECTLY ADDED HERE (0x33 hex)
        public const ushort PID_APP_TRACK_LOG_0C06 = 0x0C06; // For Dog Collar Data, decimal 3078
        public const ushort PID_APP_MULTI_PERSON_0072 = 0x0072; // For Multi-Person Data, decimal 114

        [StructLayout(LayoutKind.Sequential)] public struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct SP_DEVICE_INTERFACE_DETAIL_DATA { public uint cbSize; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DevicePath; }
        [StructLayout(LayoutKind.Sequential)] public struct OVERLAPPED { public IntPtr Internal; public IntPtr InternalHigh; public int OffsetLow; public int OffsetHigh; public IntPtr hEvent; }
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr hDevInfo);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(IntPtr hFile, [Out] byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)] public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, [Out] byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, ref OVERLAPPED lpOverlapped); // Changed lpOutBuffer
        [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);
    }

    public class GpsUsbDevice : IDisposable
    { /* ... as before with P/Invoke corrections from previous responses ... */
        private IntPtr deviceHandle = NativeMethods.INVALID_HANDLE_VALUE;
        private string? devicePath;
        private GpsPacketProcessor? packetProcessor;
        private Thread? listenThread;
        private volatile bool isListening;
        private ManualResetEvent stopEvent = new ManualResetEvent(false);
        private IntPtr overlappedEventRead;
        private IntPtr overlappedEventIoctl;
        private const int ASYNC_IOCTL_BUFFER_SIZE = NativeMethods.ASYNC_DATA_SIZE;
        private const int BULK_READ_BUFFER_SIZE = 4096;
        public bool IsConnected => deviceHandle != NativeMethods.INVALID_HANDLE_VALUE;
        public bool IsSessionStarted { get; private set; } = false;

        // Callbacks for ViewModel
        private Action<PvtDataD800>? _pvtDataHandler;
        private Action<List<TrackedEntityData>>? _multiPersonDataHandler;
        private Action<DogCollarData>? _dogCollarDataHandler;
        private Action<string>? _statusMessageHandler;
        private Action<GpsUsbPacketHeader>? _usbProtocolLayerDataHandler;


        public GpsUsbDevice(GpsPacketProcessor? processor)
        {
            this.packetProcessor = processor;
        }

        public void SetPvtDataHandler(Action<PvtDataD800> handler) => _pvtDataHandler = handler;
        public void SetMultiPersonDataHandler(Action<List<TrackedEntityData>> handler) => _multiPersonDataHandler = handler;
        public void SetDogCollarDataHandler(Action<DogCollarData> handler) => _dogCollarDataHandler = handler;
        public void SetStatusMessageHandler(Action<string> handler) => _statusMessageHandler = handler;
        public void SetUsbProtocolLayerDataHandler(Action<GpsUsbPacketHeader> handler) => _usbProtocolLayerDataHandler = handler;


        public bool Connect()
        {
            if (IsConnected) CloseHandleInternal();
            _statusMessageHandler?.Invoke("Finding GPS device...");
            Guid localGuid = NativeMethods.GUID_DEVINTERFACE_GRMNUSB; // Local copy for ref
            devicePath = FindGpsDevicePath(ref localGuid);
            if (string.IsNullOrEmpty(devicePath)) { _statusMessageHandler?.Invoke("Error: GPS USB device not found."); return false; }
            _statusMessageHandler?.Invoke($"Found GPS device at: {devicePath}");
            deviceHandle = NativeMethods.CreateFile(devicePath, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (deviceHandle == NativeMethods.INVALID_HANDLE_VALUE) { _statusMessageHandler?.Invoke($"Error: Failed to open device. Win32 Error: {Marshal.GetLastWin32Error()}"); return false; }
            overlappedEventRead = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null!);
            overlappedEventIoctl = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null!);
            overlappedEventWrite = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null!);
            if (overlappedEventRead == IntPtr.Zero || overlappedEventIoctl == IntPtr.Zero || overlappedEventWrite == IntPtr.Zero) { _statusMessageHandler?.Invoke($"Error: Failed to create event objects. Win32 Error: {Marshal.GetLastWin32Error()}"); CloseHandleInternal(); return false; }
            _statusMessageHandler?.Invoke("Device opened. Sending Start Session packet...");
            return SendStartSessionPacket();
        }
        private bool SendUsbProtocolPacket(ushort packetId, byte[]? data = null)
        { /* ... as before ... */
            if (!IsConnected) return false;
            uint payloadSize = (data == null) ? 0 : (uint)data.Length; byte[] packet = new byte[12 + payloadSize];
            packet[0] = 0x00; Array.Copy(BitConverter.GetBytes(packetId), 0, packet, 4, 2); Array.Copy(BitConverter.GetBytes(payloadSize), 0, packet, 8, 4);
            if (data != null) Array.Copy(data, 0, packet, 12, payloadSize);
            NativeMethods.OVERLAPPED overlapped = new NativeMethods.OVERLAPPED { hEvent = overlappedEventWrite }; // Need separate write event
            uint bytesWritten; bool success = NativeMethods.WriteFile(deviceHandle, packet, (uint)packet.Length, out bytesWritten, ref overlapped);
            if (!success && Marshal.GetLastWin32Error() == NativeMethods.ERROR_IO_PENDING)
            {
                _statusMessageHandler?.Invoke($"Write for USB Proto ID {packetId} pending...");
                NativeMethods.WaitForSingleObject(overlapped.hEvent, 1000); // Short timeout for writes
                success = NativeMethods.GetOverlappedResult(deviceHandle, ref overlapped, out bytesWritten, false);
            }
            if (success && bytesWritten == packet.Length) { _statusMessageHandler?.Invoke($"Sent USB Proto ID {packetId}."); return true; }
            _statusMessageHandler?.Invoke($"Error sending USB Proto ID {packetId}. Err: {Marshal.GetLastWin32Error()}"); return false;
        }
        public bool SendApplicationCommand(ushort l001_Pid, ushort commandId_AXXX_payload)
        { /* ... as before ... */
            if (!IsConnected || !IsSessionStarted) { _statusMessageHandler?.Invoke("Error: No active session for App Command."); return false; }
            byte[] commandPayload = BitConverter.GetBytes(commandId_AXXX_payload); uint payloadSize = (uint)commandPayload.Length;
            byte[] packet = new byte[12 + payloadSize]; packet[0] = 0x14;
            Array.Copy(BitConverter.GetBytes(l001_Pid), 0, packet, 4, 2); Array.Copy(BitConverter.GetBytes(payloadSize), 0, packet, 8, 4);
            Array.Copy(commandPayload, 0, packet, 12, payloadSize);
            NativeMethods.OVERLAPPED overlapped = new NativeMethods.OVERLAPPED { hEvent = overlappedEventWrite };
            uint bytesWritten; bool success = NativeMethods.WriteFile(deviceHandle, packet, (uint)packet.Length, out bytesWritten, ref overlapped);
            if (!success && Marshal.GetLastWin32Error() == NativeMethods.ERROR_IO_PENDING)
            {
                _statusMessageHandler?.Invoke($"Write for App Cmd {commandId_AXXX_payload} pending...");
                NativeMethods.WaitForSingleObject(overlapped.hEvent, 1000);
                success = NativeMethods.GetOverlappedResult(deviceHandle, ref overlapped, out bytesWritten, false);
            }
            if (success && bytesWritten == packet.Length) { _statusMessageHandler?.Invoke($"Sent App Cmd: L001 PID {l001_Pid}, Payload CMD {commandId_AXXX_payload}."); return true; }
            _statusMessageHandler?.Invoke($"Error sending App Cmd. Err: {Marshal.GetLastWin32Error()}"); return false;
        }
        private bool SendStartSessionPacket() => SendUsbProtocolPacket(NativeMethods.PID_START_SESSION);
        public bool SendStartPvtDataCommand() => SendApplicationCommand(NativeMethods.PID_COMMAND_DATA, NativeMethods.CMD_START_PVT_DATA);
        private string? FindGpsDevicePath(ref Guid classGuid)
        { /* ... as before, ensure cbSize init for SP_DEVICE_INTERFACE_DETAIL_DATA is robust, e.g. Marshal.WriteInt32(detailDataBuffer, 0, (IntPtr.Size == 8 ? 8 : 6)); ... */
            IntPtr hDevInfo = NativeMethods.SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
            if (hDevInfo == NativeMethods.INVALID_HANDLE_VALUE) { _statusMessageHandler?.Invoke($"SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}"); return null; }
            try
            {
                NativeMethods.SP_DEVICE_INTERFACE_DATA diData = new NativeMethods.SP_DEVICE_INTERFACE_DATA(); diData.cbSize = (uint)Marshal.SizeOf(diData);
                if (NativeMethods.SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref classGuid, 0, ref diData))
                {
                    uint size = 0; NativeMethods.SetupDiGetDeviceInterfaceDetail(hDevInfo, ref diData, IntPtr.Zero, 0, out size, IntPtr.Zero); if (size == 0) return null;
                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detailDataBuffer, 0, (IntPtr.Size == 8 ? 8 : (4 + Marshal.SystemDefaultCharSize))); // Common heuristic for cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA buffer
                        if (NativeMethods.SetupDiGetDeviceInterfaceDetail(hDevInfo, ref diData, detailDataBuffer, size, out _, IntPtr.Zero))
                        {
                            return Marshal.PtrToStringAuto(IntPtr.Add(detailDataBuffer, 4)); // Path starts after cbSize field
                        }
                        else { _statusMessageHandler?.Invoke($"SetupDiGetDeviceInterfaceDetail failed: {Marshal.GetLastWin32Error()}"); }
                    }
                    finally { Marshal.FreeHGlobal(detailDataBuffer); }
                }
                else { if (Marshal.GetLastWin32Error() != 259 /*NO_MORE_ITEMS*/) _statusMessageHandler?.Invoke($"SetupDiEnumDeviceInterfaces failed: {Marshal.GetLastWin32Error()}"); }
            }
            finally { NativeMethods.SetupDiDestroyDeviceInfoList(hDevInfo); }
            return null;
        }
        public void StartListening()
        {
            if (!IsConnected || isListening) return;
            isListening = true; stopEvent.Reset();
            _statusMessageHandler?.Invoke("Started listening for GPS USB data (overlapped)...");
            // Run listen loop on the calling thread so StartAsync can await it via Task.Run.
            ListenLoopOverlapped();
        }
        public void StopListening()
        {
            isListening = false; stopEvent.Set();
            _statusMessageHandler?.Invoke("Stopped listening.");
        }
        private void ListenLoopOverlapped()
        { /* ... more robust async loop as previously discussed ... */
            byte[] asyncIoBuffer = new byte[ASYNC_IOCTL_BUFFER_SIZE];
            byte[] bulkReadBuffer = new byte[BULK_READ_BUFFER_SIZE];
            NativeMethods.OVERLAPPED ovIoctl = new NativeMethods.OVERLAPPED { hEvent = overlappedEventIoctl };
            NativeMethods.OVERLAPPED ovRead = new NativeMethods.OVERLAPPED { hEvent = overlappedEventRead }; // Separate event for ReadFile
            bool ioctlPending = false;

            // Initial IOCTL Post
            if (isListening) ioctlPending = ReissueDeviceIoControl(asyncIoBuffer, ref ovIoctl);

            while (isListening)
            {
                if (!ioctlPending && isListening)
                { // If previous completed or failed, try to reissue
                    ioctlPending = ReissueDeviceIoControl(asyncIoBuffer, ref ovIoctl);
                    if (!ioctlPending) { Thread.Sleep(100); continue; } // Failed to issue, pause and retry
                }

                uint waitResult = NativeMethods.WaitForSingleObject(ovIoctl.hEvent, 100); // Poll with timeout

                if (!isListening) break;

                if (waitResult == 0x00000000L)
                { // IOCTL event signaled
                    ioctlPending = false; // Assume it completed, successfully or not
                    uint bytesTransferred;
                    if (NativeMethods.GetOverlappedResult(deviceHandle, ref ovIoctl, out bytesTransferred, false))
                    {
                        if (bytesTransferred > 0)
                        {
                            HandleAsyncData(asyncIoBuffer, bytesTransferred, bulkReadBuffer, ref ovRead);
                        }
                        // Successfully handled, immediately try to reissue if still listening
                        if (isListening) ioctlPending = ReissueDeviceIoControl(asyncIoBuffer, ref ovIoctl);
                    }
                    else
                    {
                        _statusMessageHandler?.Invoke($"GetOverlappedResult for IOCTL failed: {Marshal.GetLastWin32Error()}");
                        if (isListening) Thread.Sleep(100); // Pause on error before retrying issue
                    }
                }
                else if (waitResult != NativeMethods.WAIT_TIMEOUT)
                {
                    _statusMessageHandler?.Invoke($"WaitForSingleObject on IOCTL event: {waitResult}, Err: {Marshal.GetLastWin32Error()}");
                    if (isListening) Thread.Sleep(100);
                }
                // If timeout, loop continues, checks isListening, and will try to reissue if not pending
            }
            _statusMessageHandler?.Invoke("Listen loop ending.");
        }

        private bool ReissueDeviceIoControl(byte[] buffer, ref NativeMethods.OVERLAPPED overlapped)
        {
            if (!isListening || !IsConnected) return false; uint bytesReturned;

            if (!NativeMethods.DeviceIoControl(deviceHandle, NativeMethods.IOCTL_ASYNC_IN_CTL_CODE, IntPtr.Zero, 0, buffer, ASYNC_IOCTL_BUFFER_SIZE, out bytesReturned, ref overlapped))
            {
                if (Marshal.GetLastWin32Error() == NativeMethods.ERROR_IO_PENDING) return true;
                else { _statusMessageHandler?.Invoke($"Reissue DeviceIoControl error: {Marshal.GetLastWin32Error()}"); return false; }
            }
            else
            { // Completed synchronously
                if (bytesReturned > 0) HandleAsyncData(buffer, bytesReturned, new byte[BULK_READ_BUFFER_SIZE], ref overlapped);
                return ReissueDeviceIoControl(buffer, ref overlapped); // Re-issue to make it pending for next async event
            }
        }
        private void HandleAsyncData(byte[] asyncDataBuffer, uint bytesReceived, byte[] bulkReadBuffer, ref NativeMethods.OVERLAPPED bulkReadOv)
        { /* ... as before ... */
            // Make sure to call appropriate handlers like _dogCollarDataHandler, _pvtDataHandler, or packetProcessor
            byte[] receivedData = new byte[bytesReceived]; Array.Copy(asyncDataBuffer, receivedData, bytesReceived);
            var usbHeader = GpsUsbPacketHeader.FromBytes(receivedData);
            if (usbHeader == null) return;

            //_statusMessageHandler?.Invoke($"Async data: Type 0x{usbHeader.PacketType:X2}, ID {usbHeader.ApplicationPacketID}");

            if (usbHeader.PacketType == 0)
            { // USB Protocol Layer Packet
                _usbProtocolLayerDataHandler?.Invoke(usbHeader); // Let ViewModel/Service decide actions like sending StartPvt
                if (usbHeader.ApplicationPacketID == NativeMethods.PID_DATA_AVAILABLE)
                {
                    //_statusMessageHandler?.Invoke("Pid_Data_Available received. Reading bulk pipe...");
                    ReadBulkPipeData(bulkReadBuffer, ref bulkReadOv);
                }
                else if (usbHeader.ApplicationPacketID == NativeMethods.PID_SESSION_STARTED)
                {
                    IsSessionStarted = true; // This flag is now class level
                                             // Unit ID is in payload here
                    _statusMessageHandler?.Invoke("Session Started by device.");
                }
            }
            else if (usbHeader.PacketType == 0x14)
            { // Application Layer Packet
              // Extract the actual Garmin payload based on usbHeader.PayloadDataSize
              // The 'receivedData' here is the full USB packet including its 12-byte header
                byte[] fullGpsUsbPacket = new byte[12 + usbHeader.PayloadDataSize];
                if (receivedData.Length >= fullGpsUsbPacket.Length)
                {
                    Array.Copy(receivedData, fullGpsUsbPacket, fullGpsUsbPacket.Length);
                    DispatchApplicationPacket(fullGpsUsbPacket);
                }
                else
                {
                    _statusMessageHandler?.Invoke($"Async App packet too short. Have {receivedData.Length}, need {fullGpsUsbPacket.Length}");
                }
            }
        }
        private void ReadBulkPipeData(byte[] bulkBuffer, ref NativeMethods.OVERLAPPED overlapped)
        { /* ... as before, ensure DispatchApplicationPacket is called ... */
            bool keepReading = true;
            while (isListening && keepReading)
            {
                uint bytesReadFromBulk = 0;
                bool success = NativeMethods.ReadFile(deviceHandle, bulkBuffer, (uint)bulkBuffer.Length, out bytesReadFromBulk, ref overlapped);
                if (!success && Marshal.GetLastWin32Error() == NativeMethods.ERROR_IO_PENDING)
                {
                    //_statusMessageHandler?.Invoke("ReadFile (Bulk) pending...");
                    uint waitResult = NativeMethods.WaitForSingleObject(overlapped.hEvent, 2000); // 2s timeout
                    if (waitResult == 0x00000000L) success = NativeMethods.GetOverlappedResult(deviceHandle, ref overlapped, out bytesReadFromBulk, false);
                    else { _statusMessageHandler?.Invoke($"ReadFile (Bulk) wait failed/timeout: {waitResult}, Err:{Marshal.GetLastWin32Error()}"); keepReading = false; continue; }
                }
                if (success)
                {
                    if (bytesReadFromBulk == 0) { keepReading = false; } // Normal USB bulk transfer termination
                    else
                    {
                        //_statusMessageHandler?.Invoke($"ReadFile (Bulk) got {bytesReadFromBulk} bytes.");
                        byte[] actualBulkData = new byte[bytesReadFromBulk]; Array.Copy(bulkBuffer, actualBulkData, bytesReadFromBulk);
                        DispatchApplicationPacket(actualBulkData); // This IS the full USB packet (header + payload)
                    }
                }
                else { _statusMessageHandler?.Invoke($"ReadFile (Bulk) error: {Marshal.GetLastWin32Error()}"); keepReading = false; }
            }
        }

        // New method to dispatch based on actual application payload
        private void DispatchApplicationPacket(byte[] fullUsbPacketWithHeader)
        {
            var usbHeader = GpsUsbPacketHeader.FromBytes(fullUsbPacketWithHeader);
            if (usbHeader == null || usbHeader.PacketType != 0x14) return;

            int actualPayloadLength = fullUsbPacketWithHeader.Length - 12;
            if (actualPayloadLength < usbHeader.PayloadDataSize)
            {
                _statusMessageHandler?.Invoke($"Dispatch Error: Payload truncated. Declared: {usbHeader.PayloadDataSize}, Has: {actualPayloadLength}");
                return;
            }
            byte[] payload = new byte[usbHeader.PayloadDataSize];
            Array.Copy(fullUsbPacketWithHeader, 12, payload, 0, usbHeader.PayloadDataSize);


            switch (usbHeader.ApplicationPacketID)
            {
                case NativeMethods.PID_PVT_DATA: // 0x33 (51)
                    if (usbHeader.PayloadDataSize == 64 && payload.Length >= 60)
                    {
                        var pvt = PvtDataD800.FromPayload(payload);
                        _pvtDataHandler?.Invoke(pvt);
                        // Keine Koordinaten/UTM/Positionsdaten in StatusMessage!
                    }
                    // Keine Statusmeldung bei Paketgrößenabweichung, nur Log
                    break;
                case 0x0C06: // Dog collar
                    if (usbHeader.PayloadDataSize == 100 && payload.Length == 100)
                    {
                        var dogData = DogCollarData.FromPayload(payload);
                        if (dogData != null)
                        {
                            _dogCollarDataHandler?.Invoke(dogData);
                            // Keine StatusMessage für Hundedaten
                        }
                    }
                    // Keine Statusmeldung bei Paketgrößenabweichung, nur Log
                    break;
                case 0x0072: // Multi-person
                    if (usbHeader.PayloadDataSize == 72 && payload.Length == 72)
                    {
                        List<TrackedEntityData> entities = new List<TrackedEntityData>();
                        for (int i = 0; i < payload.Length / 12; i++)
                        {
                            var entity = TrackedEntityData.FromBytes(payload, i * 12);
                            if (entity != null)
                                entities.Add(entity);
                        }
                        _multiPersonDataHandler?.Invoke(entities);
                        // Keine StatusMessage
                    }
                    else if (usbHeader.PayloadDataSize != payload.Length)
                    {
                        // Optional: Log nur bei echtem Fehler
                        _statusMessageHandler?.Invoke($"Multi-Person packet size mismatch. Declared: {usbHeader.PayloadDataSize}, Have: {payload.Length}");
                    }
                    break;
                default:
                    _statusMessageHandler?.Invoke($"Received unhandled AppID: 0x{usbHeader.ApplicationPacketID:X4}");
                    // Optionally pass to generic packet processor if it's set
                    // this.packetProcessor.ProcessPacket(fullUsbPacketWithHeader); 
                    break;
            }
        }

        private IntPtr overlappedEventWrite; // Added for Write operations
        private void CloseHandleInternal()
        {
            if (deviceHandle != NativeMethods.INVALID_HANDLE_VALUE) { NativeMethods.CloseHandle(deviceHandle); deviceHandle = NativeMethods.INVALID_HANDLE_VALUE; }
            if (overlappedEventRead != IntPtr.Zero) { NativeMethods.CloseHandle(overlappedEventRead); overlappedEventRead = IntPtr.Zero; }
            if (overlappedEventIoctl != IntPtr.Zero) { NativeMethods.CloseHandle(overlappedEventIoctl); overlappedEventIoctl = IntPtr.Zero; }
            if (overlappedEventWrite != IntPtr.Zero) { NativeMethods.CloseHandle(overlappedEventWrite); overlappedEventWrite = IntPtr.Zero; } // Close write event
        }
        public void Dispose()
        { /* ... as before ... */
            StopListening(); CloseHandleInternal(); stopEvent?.Dispose();
            _statusMessageHandler?.Invoke("GpsUsbDevice disposed.");
        }
    }

}
