using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace HkuLibrarySeatBooking
{
    internal sealed class BookingHarRecorder : IDisposable
    {
        private readonly object sync = new object();
        private readonly List<HarEntry> entries = new List<HarEntry>();
        private readonly DateTime releaseAt;
        private readonly DateTime recordingStartedAt;
        private bool saved;

        public BookingHarRecorder(DateTime releaseAt)
            : this(
                releaseAt,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HKU-Library-Seat-Booking",
                    "har",
                    "booking-" +
                    releaseAt.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) +
                    "-" +
                    DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) +
                    ".har"))
        {
        }

        internal BookingHarRecorder(DateTime releaseAt, string filePath)
        {
            this.releaseAt = releaseAt;
            this.recordingStartedAt = DateTime.Now;
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            this.FilePath = filePath;
        }

        public string FilePath { get; private set; }

        public void RecordSuccess(
            string method,
            string url,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            string requestContentType,
            string requestBody,
            CookieCollection requestCookies,
            HttpWebResponse response,
            HttpResponseData responseData)
        {
            lock (this.sync)
            {
                try
                {
                    this.entries.Add(this.CreateEntry(
                        method,
                        url,
                        startedAtUtc,
                        endedAtUtc,
                        requestContentType,
                        requestBody,
                        requestCookies,
                        response,
                        responseData,
                        null));
                }
                catch
                {
                }
            }
        }

        public void RecordFailure(
            string method,
            string url,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            string requestContentType,
            string requestBody,
            CookieCollection requestCookies,
            HttpWebResponse errorResponse,
            HttpResponseData errorData,
            WebException exception)
        {
            lock (this.sync)
            {
                try
                {
                    this.entries.Add(this.CreateEntry(
                        method,
                        url,
                        startedAtUtc,
                        endedAtUtc,
                        requestContentType,
                        requestBody,
                        requestCookies,
                        errorResponse,
                        errorData,
                        exception));
                }
                catch
                {
                }
            }
        }

        public void Save()
        {
            lock (this.sync)
            {
                if (this.saved)
                {
                    return;
                }
                this.saved = true;

                HarRoot root = new HarRoot
                {
                    Log = new HarLog
                    {
                        Version = "1.2",
                        Creator = new HarCreator
                        {
                            Name = "HKU Library Seat Booking",
                            Version = "0.1.6"
                        },
                        ReleaseAt = this.releaseAt.ToString("o", CultureInfo.InvariantCulture),
                        RecordingStartedAt = this.recordingStartedAt.ToString(
                            "o",
                            CultureInfo.InvariantCulture),
                        RecordingFinishedAt = DateTime.Now.ToString(
                            "o",
                            CultureInfo.InvariantCulture),
                        Entries = new List<HarEntry>(this.entries)
                    }
                };

                string temporaryPath = this.FilePath + ".tmp";
                using (FileStream stream = File.Create(temporaryPath))
                {
                    DataContractJsonSerializer serializer =
                        new DataContractJsonSerializer(typeof(HarRoot));
                    serializer.WriteObject(stream, root);
                    stream.Flush(true);
                }

                if (File.Exists(this.FilePath))
                {
                    File.Replace(temporaryPath, this.FilePath, null);
                }
                else
                {
                    File.Move(temporaryPath, this.FilePath);
                }
            }
        }

        public void Dispose()
        {
            try
            {
                this.Save();
            }
            catch
            {
            }
        }

        private HarEntry CreateEntry(
            string method,
            string url,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            string requestContentType,
            string requestBody,
            CookieCollection requestCookies,
            HttpWebResponse response,
            HttpResponseData responseData,
            WebException exception)
        {
            Uri uri = new Uri(url);
            TimeSpan elapsed = endedAtUtc - startedAtUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            List<HarHeader> requestHeaders = new List<HarHeader>
            {
                new HarHeader { Name = "Host", Value = uri.Host },
                new HarHeader { Name = "Accept", Value = "text/html,application/xhtml+xml" },
                new HarHeader { Name = "Cookie", Value = "[redacted]" }
            };
            if (!string.IsNullOrWhiteSpace(requestContentType))
            {
                requestHeaders.Add(new HarHeader
                {
                    Name = "Content-Type",
                    Value = requestContentType
                });
            }

            List<HarNameValue> cookies = new List<HarNameValue>();
            if (requestCookies != null)
            {
                foreach (Cookie cookie in requestCookies)
                {
                    cookies.Add(new HarNameValue
                    {
                        Name = cookie.Name,
                        Value = "[redacted]"
                    });
                }
            }

            HarRequest request = new HarRequest
            {
                Method = method,
                Url = RedactUrl(url),
                HttpVersion = "HTTP/1.1",
                Headers = requestHeaders,
                QueryString = ParseQuery(uri),
                Cookies = cookies,
                HeadersSize = -1,
                BodySize = requestBody == null ? 0 : Encoding.UTF8.GetByteCount(requestBody)
            };
            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                request.PostData = new HarPostData
                {
                    MimeType = requestContentType ?? "application/x-www-form-urlencoded",
                    Text = RedactFormBody(requestBody)
                };
            }

            HarResponse harResponse;
            if (response != null && responseData != null)
            {
                int contentBytes = string.IsNullOrEmpty(responseData.Content)
                    ? 0
                    : Encoding.UTF8.GetByteCount(responseData.Content);
                harResponse = new HarResponse
                {
                    Status = responseData.StatusCode,
                    StatusText = response.StatusDescription ?? string.Empty,
                    HttpVersion = "HTTP/1.1",
                    Headers = ReadResponseHeaders(response),
                    Cookies = ReadResponseCookies(response),
                    Content = new HarContent
                    {
                        Size = contentBytes,
                        MimeType = responseData.ContentType ?? "application/octet-stream",
                        Comment = "Response body omitted to avoid personal data."
                    },
                    RedirectUrl = response.Headers["Location"] ?? string.Empty,
                    HeadersSize = -1,
                    BodySize = contentBytes
                };
            }
            else
            {
                harResponse = new HarResponse
                {
                    Status = 0,
                    StatusText = GetFailureStatus(exception),
                    HttpVersion = "HTTP/1.1",
                    Headers = new List<HarHeader>(),
                    Cookies = new List<HarNameValue>(),
                    Content = new HarContent
                    {
                        Size = 0,
                        MimeType = "x-unknown"
                    },
                    RedirectUrl = string.Empty,
                    HeadersSize = -1,
                    BodySize = 0
                };
            }

            return new HarEntry
            {
                StartedDateTime = startedAtUtc.ToString("o", CultureInfo.InvariantCulture),
                Time = elapsed.TotalMilliseconds,
                Request = request,
                Response = harResponse,
                Cache = new HarCache(),
                Timings = new HarTimings
                {
                    Send = 0,
                    Wait = elapsed.TotalMilliseconds,
                    Receive = 0
                },
                Comment = exception == null
                    ? "Sensitive values redacted."
                    : "Request failed: " + exception.Status + ": " + exception.Message
            };
        }

        private static string RedactUrl(string url)
        {
            Uri uri = new Uri(url);
            if (string.IsNullOrEmpty(uri.Query))
            {
                return url;
            }

            StringBuilder builder = new StringBuilder();
            foreach (string pair in uri.Query.TrimStart('?').Split('&'))
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }
                int equals = pair.IndexOf('=');
                string key = equals < 0 ? pair : pair.Substring(0, equals);
                builder.Append(key);
                builder.Append('=');
                builder.Append(IsSafeFormName(Uri.UnescapeDataString(key))
                    ? (equals < 0 ? string.Empty : pair.Substring(equals + 1))
                    : "[redacted]");
            }
            UriBuilder safe = new UriBuilder(uri);
            safe.Query = builder.ToString();
            return safe.Uri.ToString();
        }

        private static List<HarNameValue> ParseQuery(Uri uri)
        {
            List<HarNameValue> values = new List<HarNameValue>();
            if (string.IsNullOrEmpty(uri.Query))
            {
                return values;
            }

            foreach (string pair in uri.Query.TrimStart('?').Split('&'))
            {
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }
                int equals = pair.IndexOf('=');
                string name = Uri.UnescapeDataString(
                    equals < 0 ? pair : pair.Substring(0, equals));
                values.Add(new HarNameValue
                {
                    Name = name,
                    Value = IsSafeFormName(name) && equals >= 0
                        ? Uri.UnescapeDataString(pair.Substring(equals + 1))
                        : "[redacted]"
                });
            }
            return values;
        }

        private static string RedactFormBody(string body)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string pair in body.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                int equals = pair.IndexOf('=');
                string encodedName = equals < 0 ? pair : pair.Substring(0, equals);
                string name = Uri.UnescapeDataString(encodedName);
                builder.Append(encodedName);
                builder.Append('=');
                if (equals >= 0 && IsSafeFormName(name))
                {
                    builder.Append(pair.Substring(equals + 1));
                }
                else
                {
                    builder.Append(Uri.EscapeDataString("[redacted]"));
                }
            }
            return builder.ToString();
        }

        private static bool IsSafeFormName(string name)
        {
            string value = (name ?? string.Empty).ToLowerInvariant();
            if (value.Contains("ddlview") ||
                value.Contains("ddllibrary") ||
                value.Contains("ddltype") ||
                value.Contains("ddlfloor") ||
                value.Contains("ddlfacility") ||
                value.Contains("listsession") ||
                value.EndsWith("btnsubmit", StringComparison.Ordinal) ||
                value.EndsWith("btnsubmityes", StringComparison.Ordinal) ||
                value.EndsWith("btncloseresult", StringComparison.Ordinal) ||
                value.EndsWith("btngetresult", StringComparison.Ordinal) ||
                value == "library" ||
                value == "ftype" ||
                value == "facility" ||
                value == "date" ||
                value == "session")
            {
                return true;
            }
            return false;
        }

        private static List<HarHeader> ReadResponseHeaders(HttpWebResponse response)
        {
            List<HarHeader> headers = new List<HarHeader>();
            foreach (string name in response.Headers.AllKeys)
            {
                if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(new HarHeader
                    {
                        Name = name,
                        Value = "[redacted]"
                    });
                }
                else
                {
                    headers.Add(new HarHeader
                    {
                        Name = name,
                        Value = response.Headers[name] ?? string.Empty
                    });
                }
            }
            return headers;
        }

        private static List<HarNameValue> ReadResponseCookies(HttpWebResponse response)
        {
            List<HarNameValue> cookies = new List<HarNameValue>();
            string value = response.Headers["Set-Cookie"];
            if (string.IsNullOrWhiteSpace(value))
            {
                return cookies;
            }

            int equals = value.IndexOf('=');
            if (equals > 0)
            {
                cookies.Add(new HarNameValue
                {
                    Name = value.Substring(0, equals).Trim(),
                    Value = "[redacted]"
                });
            }
            return cookies;
        }

        private static string GetFailureStatus(WebException exception)
        {
            return exception == null ? "ERROR" : exception.Status.ToString();
        }
    }

    [DataContract]
    internal sealed class HarRoot
    {
        [DataMember(Name = "log")]
        public HarLog Log { get; set; }
    }

    [DataContract]
    internal sealed class HarLog
    {
        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "creator")]
        public HarCreator Creator { get; set; }

        [DataMember(Name = "entries")]
        public List<HarEntry> Entries { get; set; }

        [DataMember(Name = "_releaseAt")]
        public string ReleaseAt { get; set; }

        [DataMember(Name = "_recordingStartedAt")]
        public string RecordingStartedAt { get; set; }

        [DataMember(Name = "_recordingFinishedAt")]
        public string RecordingFinishedAt { get; set; }
    }

    [DataContract]
    internal sealed class HarCreator
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }
    }

    [DataContract]
    internal sealed class HarEntry
    {
        [DataMember(Name = "startedDateTime")]
        public string StartedDateTime { get; set; }

        [DataMember(Name = "time")]
        public double Time { get; set; }

        [DataMember(Name = "request")]
        public HarRequest Request { get; set; }

        [DataMember(Name = "response")]
        public HarResponse Response { get; set; }

        [DataMember(Name = "cache")]
        public HarCache Cache { get; set; }

        [DataMember(Name = "timings")]
        public HarTimings Timings { get; set; }

        [DataMember(Name = "comment")]
        public string Comment { get; set; }
    }

    [DataContract]
    internal sealed class HarRequest
    {
        [DataMember(Name = "method")]
        public string Method { get; set; }

        [DataMember(Name = "url")]
        public string Url { get; set; }

        [DataMember(Name = "httpVersion")]
        public string HttpVersion { get; set; }

        [DataMember(Name = "headers")]
        public List<HarHeader> Headers { get; set; }

        [DataMember(Name = "queryString")]
        public List<HarNameValue> QueryString { get; set; }

        [DataMember(Name = "cookies")]
        public List<HarNameValue> Cookies { get; set; }

        [DataMember(Name = "headersSize")]
        public int HeadersSize { get; set; }

        [DataMember(Name = "bodySize")]
        public int BodySize { get; set; }

        [DataMember(Name = "postData", EmitDefaultValue = false)]
        public HarPostData PostData { get; set; }
    }

    [DataContract]
    internal sealed class HarResponse
    {
        [DataMember(Name = "status")]
        public int Status { get; set; }

        [DataMember(Name = "statusText")]
        public string StatusText { get; set; }

        [DataMember(Name = "httpVersion")]
        public string HttpVersion { get; set; }

        [DataMember(Name = "headers")]
        public List<HarHeader> Headers { get; set; }

        [DataMember(Name = "cookies")]
        public List<HarNameValue> Cookies { get; set; }

        [DataMember(Name = "content")]
        public HarContent Content { get; set; }

        [DataMember(Name = "redirectURL")]
        public string RedirectUrl { get; set; }

        [DataMember(Name = "headersSize")]
        public int HeadersSize { get; set; }

        [DataMember(Name = "bodySize")]
        public int BodySize { get; set; }
    }

    [DataContract]
    internal sealed class HarHeader
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }
    }

    [DataContract]
    internal sealed class HarNameValue
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }
    }

    [DataContract]
    internal sealed class HarPostData
    {
        [DataMember(Name = "mimeType")]
        public string MimeType { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }
    }

    [DataContract]
    internal sealed class HarContent
    {
        [DataMember(Name = "size")]
        public int Size { get; set; }

        [DataMember(Name = "mimeType")]
        public string MimeType { get; set; }

        [DataMember(Name = "comment", EmitDefaultValue = false)]
        public string Comment { get; set; }
    }

    [DataContract]
    internal sealed class HarTimings
    {
        [DataMember(Name = "send")]
        public double Send { get; set; }

        [DataMember(Name = "wait")]
        public double Wait { get; set; }

        [DataMember(Name = "receive")]
        public double Receive { get; set; }
    }

    [DataContract]
    internal sealed class HarCache
    {
    }
}
