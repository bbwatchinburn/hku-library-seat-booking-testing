using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace HkuLibrarySeatBooking
{
    internal interface ISecretProtector
    {
        string Protect(string plainText);
        string Unprotect(string protectedText);
    }

    internal sealed class DpapiSecretProtector : ISecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HKU.Library.Seat.Booking.v1");

        public string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            byte[] clear = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(clear, 0, clear.Length);
            return Convert.ToBase64String(encrypted);
        }

        public string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText))
            {
                return string.Empty;
            }

            byte[] encrypted = Convert.FromBase64String(protectedText);
            byte[] clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(clear);
            }
            finally
            {
                Array.Clear(clear, 0, clear.Length);
            }
        }
    }

    internal sealed class ConfigStore
    {
        private readonly string filePath;
        private readonly ISecretProtector protector;

        public ConfigStore(string filePath)
            : this(filePath, new DpapiSecretProtector())
        {
        }

        public ConfigStore(string filePath, ISecretProtector protector)
        {
            this.filePath = filePath;
            this.protector = protector;
        }

        public string FilePath
        {
            get { return this.filePath; }
        }

        public BookingConfig Load()
        {
            if (!File.Exists(this.filePath))
            {
                return null;
            }

            try
            {
                using (FileStream stream = File.OpenRead(this.filePath))
                {
                    DataContractJsonSerializer serializer =
                        new DataContractJsonSerializer(typeof(BookingConfig));
                    BookingConfig config = serializer.ReadObject(stream) as BookingConfig;
                    if (config == null)
                    {
                        return null;
                    }
                    config.Normalize();
                    return config;
                }
            }
            catch
            {
                return null;
            }
        }

        public void Save(BookingConfig config)
        {
            config.Normalize();
            string directory = Path.GetDirectoryName(this.filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = this.filePath + ".tmp";
            using (FileStream stream = File.Create(temporaryPath))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(BookingConfig));
                serializer.WriteObject(stream, config);
                stream.Flush(true);
            }

            if (File.Exists(this.filePath))
            {
                File.Replace(temporaryPath, this.filePath, null);
            }
            else
            {
                File.Move(temporaryPath, this.filePath);
            }
        }

        public string ReadPassword(BookingConfig config)
        {
            try
            {
                return this.protector.Unprotect(config.ProtectedPassword);
            }
            catch
            {
                return string.Empty;
            }
        }

        public string ReadCookie(BookingConfig config)
        {
            try
            {
                return this.protector.Unprotect(config.ProtectedCookie);
            }
            catch
            {
                return string.Empty;
            }
        }

        public void SetPassword(BookingConfig config, string password)
        {
            config.ProtectedPassword = this.protector.Protect(password);
        }

        public void SetCookie(BookingConfig config, string cookie)
        {
            config.ProtectedCookie = this.protector.Protect(cookie);
        }
    }
}
