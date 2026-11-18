using System.Security.Cryptography;
using System.Text;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Prüft Update-Pakete vor der Installation (Issue 211): Signatur mit dem Release-Schlüssel (ECDSA P-256, SHA-256)
    /// und SHA-256-Prüfsumme. Der private Schlüssel liegt nur auf dem Release-Rechner, nicht bei GitHub – ein
    /// übernommenes GitHub-Konto kann so kein Paket unterschieben, das die Server installieren.
    /// Signieren: openssl dgst -sha256 -sign release-signatur.pem -out paket.zip.sig paket.zip (Skill release).
    /// </summary>
    public static class UpdatePaketPruefung
    {
        /// <summary>Öffentliche Release-Schlüssel (SubjectPublicKeyInfo, Base64). Für einen Schlüsselwechsel neuen ergänzen.</summary>
        public static readonly IReadOnlyList<string> ReleaseSchluessel =
        [
            // Erzeugt 18.11.2026 auf dem Mac (~/.einsatzspur/release-signatur.pem)
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAExm2WJVCNTKGPedZiYEIJRUXp8Kizufh8sN6RuUrJOgJGT4jYj0Mi7wCDvulcAfH/8J6jGw+qObCeUPiRQzvBiQ=="
        ];

        public const string SignaturEndung = ".sig";
        public const string PruefsummeEndung = ".sha256";

        /// <summary>true, wenn die Signatur (DER, wie openssl sie schreibt) zu einem der Schlüssel passt.</summary>
        public static bool SignaturGueltig(byte[] paket, byte[] signatur, IEnumerable<string>? schluessel = null)
        {
            if (paket.Length == 0 || signatur.Length == 0) return false;
            foreach (var spki in schluessel ?? ReleaseSchluessel)
            {
                try
                {
                    using var ecdsa = ECDsa.Create();
                    ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spki), out _);
                    if (ecdsa.VerifyData(paket, signatur, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                        return true;
                }
                catch (CryptographicException) { /* kaputte Signatur oder Schlüssel: nächsten versuchen */ }
                catch (FormatException) { }
            }
            return false;
        }

        /// <summary>true, wenn die erste Angabe der Prüfsummendatei (Format von sha256sum) zum Paket passt.</summary>
        public static bool PruefsummeGueltig(byte[] paket, byte[] pruefsummeDatei)
        {
            var text = Encoding.ASCII.GetString(pruefsummeDatei).Trim();
            var erwartet = text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (erwartet is null || erwartet.Length != 64) return false;
            var ist = Convert.ToHexString(SHA256.HashData(paket));
            return string.Equals(ist, erwartet, StringComparison.OrdinalIgnoreCase);
        }
    }
}
