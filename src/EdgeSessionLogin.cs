using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HkuLibrarySeatBooking
{
    internal static class EdgeSessionLogin
    {
        private const string BookingUrl = "https://booking.lib.hku.hk/";

        public static string LoginAndCaptureCookie(
            Action<string> reportStatus,
            CancellationToken cancellationToken)
        {
            string edgePath = FindEdgePath();
            string profileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HKU-Library-Seat-Booking",
                "EdgeProfile");
            Directory.CreateDirectory(profileDirectory);

            int debugPort = FindAvailablePort();
            Process edgeProcess = null;
            ClientWebSocket socket = null;
            try
            {
                reportStatus("正在打开专用 Edge 登录窗口...");
                string arguments =
                    "--remote-debugging-address=127.0.0.1 " +
                    "--remote-debugging-port=" + debugPort + " " +
                    "--user-data-dir=\"" + profileDirectory.Replace("\"", string.Empty) + "\" " +
                    "--no-first-run --no-default-browser-check " +
                    "--new-window \"" + BookingUrl + "\"";
                edgeProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = arguments,
                    UseShellExecute = false
                });

                string websocketUrl = WaitForPageWebSocket(
                    debugPort,
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
                socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                socket.ConnectAsync(
                    new Uri(websocketUrl),
                    cancellationToken).GetAwaiter().GetResult();

                SendCommand(socket, 1, "Network.enable", "{}", cancellationToken);
                SendCommand(socket, 2, "Runtime.enable", "{}", cancellationToken);
                reportStatus("请在 Edge 中完成 HKU 登录；完成后会自动导入会话。");

                DateTime deadline = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CdpResult location = SendCommand(
                        socket,
                        3,
                        "Runtime.evaluate",
                        "{\"expression\":\"location.href\",\"returnByValue\":true}",
                        cancellationToken);
                    string currentUrl = location != null &&
                        location.Value != null &&
                        location.Value.Value != null
                            ? location.Value.Value
                            : string.Empty;
                    if (currentUrl.IndexOf(
                        "booking.lib.hku.hk/Secure/",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        CdpResult cookieResult = SendCommand(
                            socket,
                            4,
                            "Network.getAllCookies",
                            "{}",
                            cancellationToken);
                        string cookieHeader = BuildCookieHeader(cookieResult.Cookies);
                        if (!string.IsNullOrWhiteSpace(cookieHeader))
                        {
                            reportStatus("已自动导入登录会话。");
                            TryCloseBrowser(socket, cancellationToken);
                            return cookieHeader;
                        }
                    }

                    cancellationToken.WaitHandle.WaitOne(1000);
                }

                throw new TimeoutException("等待 HKU 登录超时，请重新点击登录按钮。");
            }
            finally
            {
                if (socket != null)
                {
                    socket.Dispose();
                }
                if (edgeProcess != null)
                {
                    try
                    {
                        if (!edgeProcess.HasExited)
                        {
                            edgeProcess.CloseMainWindow();
                            if (!edgeProcess.WaitForExit(2000))
                            {
                                edgeProcess.Kill();
                            }
                        }
                    }
                    catch
                    {
                    }
                    edgeProcess.Dispose();
                }
            }
        }

        private static string WaitForPageWebSocket(
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            string endpoint = "http://127.0.0.1:" + port + "/json/list";
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
                    request.Proxy = null;
                    request.Timeout = 2000;
                    request.ReadWriteTimeout = 2000;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (StreamReader reader = new StreamReader(
                        response.GetResponseStream(),
                        Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        string selected = FindBookingWebSocket(json);
                        if (!string.IsNullOrWhiteSpace(selected))
                        {
                            return selected;
                        }
                    }
                }
                catch (WebException)
                {
                }
                catch (IOException)
                {
                }

                cancellationToken.WaitHandle.WaitOne(300);
            }

            throw new TimeoutException("无法连接专用 Edge 登录窗口的调试接口。");
        }

        private static string FindBookingWebSocket(string json)
        {
            foreach (Match match in Regex.Matches(
                json ?? string.Empty,
                @"\{[^{}]*\}",
                RegexOptions.Singleline))
            {
                string candidate = match.Value;
                Match webSocket = Regex.Match(
                    candidate,
                    "\"webSocketDebuggerUrl\"\\s*:\\s*\"(?<url>[^\"]+)\"",
                    RegexOptions.IgnoreCase);
                if (!webSocket.Success)
                {
                    continue;
                }

                string value = Regex.Unescape(webSocket.Groups["url"].Value);
                if (candidate.IndexOf(
                    "booking.lib.hku.hk",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return value;
                }
            }
            return null;
        }

        private static CdpResult SendCommand(
            ClientWebSocket socket,
            int id,
            string method,
            string parameters,
            CancellationToken cancellationToken)
        {
            string command =
                "{\"id\":" + id +
                ",\"method\":\"" + method +
                "\",\"params\":" + (parameters ?? "{}") + "}";
            byte[] payload = Encoding.UTF8.GetBytes(command);
            socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Text,
                true,
                cancellationToken).GetAwaiter().GetResult();

            while (!cancellationToken.IsCancellationRequested)
            {
                string message = ReceiveText(socket, cancellationToken);
                CdpMessage response = Deserialize<CdpMessage>(message);
                if (response == null || response.Id != id)
                {
                    continue;
                }
                if (response.Error != null)
                {
                    throw new InvalidOperationException(
                        "浏览器会话接口返回错误：" + response.Error.Message);
                }
                return response.Result ?? new CdpResult();
            }

            throw new OperationCanceledException(cancellationToken);
        }

        private static string ReceiveText(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using (MemoryStream stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken).GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new IOException("Edge 登录会话已关闭。");
                    }
                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T Deserialize<T>(string json) where T : class
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(stream) as T;
            }
        }

        private static string BuildCookieHeader(IList<CdpCookie> cookies)
        {
            if (cookies == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            HashSet<string> emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (CdpCookie cookie in cookies)
            {
                if (cookie == null ||
                    string.IsNullOrWhiteSpace(cookie.Name) ||
                    cookie.Value == null ||
                    (cookie.Domain ?? string.Empty).IndexOf(
                        "hku.hk",
                        StringComparison.OrdinalIgnoreCase) < 0 ||
                    !emitted.Add(cookie.Name))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }
                builder.Append(cookie.Name);
                builder.Append('=');
                builder.Append(cookie.Value);
            }
            return builder.ToString();
        }

        private static void TryCloseBrowser(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(
                    "{\"id\":99,\"method\":\"Browser.close\"}");
                socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        private static string FindEdgePath()
        {
            string[] candidates =
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft",
                    "Edge",
                    "Application",
                    "msedge.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft",
                    "Edge",
                    "Application",
                    "msedge.exe")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new FileNotFoundException("未找到 Microsoft Edge。");
        }

        private static int FindAvailablePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    [DataContract]
    internal sealed class CdpMessage
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "result")]
        public CdpResult Result { get; set; }

        [DataMember(Name = "error")]
        public CdpError Error { get; set; }
    }

    [DataContract]
    internal sealed class CdpResult
    {
        [DataMember(Name = "result")]
        public CdpRemoteValue Value { get; set; }

        [DataMember(Name = "cookies")]
        public List<CdpCookie> Cookies { get; set; }
    }

    [DataContract]
    internal sealed class CdpRemoteValue
    {
        [DataMember(Name = "value")]
        public string Value { get; set; }
    }

    [DataContract]
    internal sealed class CdpCookie
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }

        [DataMember(Name = "domain")]
        public string Domain { get; set; }
    }

    [DataContract]
    internal sealed class CdpError
    {
        [DataMember(Name = "message")]
        public string Message { get; set; }
    }
}
