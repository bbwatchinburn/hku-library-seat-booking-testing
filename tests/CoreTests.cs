using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HkuLibrarySeatBooking
{
    internal static class CoreTests
    {
        private static int passed;
        private static readonly List<string> failures = new List<string>();

        public static int Run(string reportPath)
        {
            passed = 0;
            failures.Clear();
            RunTest("weekday release and target", TestWeekdayRelease);
            RunTest("post-release grace window", TestReleaseGrace);
            RunTest("weekend slot mapping", TestWeekendSlots);
            RunTest("law library second slot mapping", TestLawLibrarySecondSlot);
            RunTest("chi wah hourly slot mapping", TestChiWahHourlySlots);
            RunTest("countdown format", TestCountdownFormat);
            RunTest("form auto-detection", TestFormDetection);
            RunTest("explicit form overrides", TestExplicitOverrides);
            RunTest("select options and postback link", TestSelectAndPostback);
            RunTest("login form detection", TestLoginForm);
            RunTest("booking confirmation button detection", TestBookingConfirmationButton);
            RunTest("config normalization", TestConfigNormalization);
            RunTest("config persistence protects secrets", TestConfigPersistence);
            RunTest("target summary", TestTargetSummary);
            RunTest("multi-sample server clock calibration", TestServerClockCalibration);
            RunTest("multi-step ASP.NET booking flow", TestMultiStepBookingFlow);

            StringBuilder report = new StringBuilder();
            report.AppendLine("HKU Library Seat Booking core tests");
            report.AppendLine("Passed: " + passed);
            report.AppendLine("Failed: " + failures.Count);
            foreach (string failure in failures)
            {
                report.AppendLine("FAIL: " + failure);
            }
            string text = report.ToString();

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                string directory = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(reportPath, text, Encoding.UTF8);
            }
            try
            {
                Console.WriteLine(text);
            }
            catch
            {
            }
            return failures.Count == 0 ? 0 : 1;
        }

        private static void TestWeekdayRelease()
        {
            BookingConfig config = BookingConfig.CreateDefault();
            config.ReleaseTime = "00:00";
            config.TargetDayOffset = 1;
            BookingAttempt attempt = ReleaseSchedule.Plan(
                config,
                new DateTime(2026, 11, 8, 10, 30, 0));
            Equal(new DateTime(2026, 11, 9, 0, 0, 0), attempt.ReleaseAt, "release date");
            Equal(new DateTime(2026, 11, 10), attempt.TargetDate, "target date");
            Equal("08:30", attempt.Slot.StartText, "weekday first-slot start");
            Equal("13:00", attempt.Slot.EndText, "weekday first-slot end");
        }

        private static void TestWeekendSlots()
        {
            BookingConfig config = BookingConfig.CreateDefault();
            config.TimeSlotIndex = 0;
            BookingSlot saturdaySlot = BookingSlots.Resolve(config, new DateTime(2026, 11, 14));
            Equal("09:00", saturdaySlot.StartText, "weekend slot 1 start");
            Equal("13:00", saturdaySlot.EndText, "weekend slot 1 end");

            config.TimeSlotIndex = 2;
            BookingSlot unavailable = BookingSlots.Resolve(config, new DateTime(2026, 11, 15));
            Equal("13:00", unavailable.StartText, "weekend falls back to slot 2");
            Equal("17:00", unavailable.EndText, "weekend slot 2 end");
        }

        private static void TestLawLibrarySecondSlot()
        {
            BookingConfig config = BookingConfig.CreateDefault();
            config.AreaName = "Law Library Study Tables";
            config.ReleaseTime = "18:00";
            config.TargetDayOffset = 1;
            config.TimeSlotIndex = 1;
            BookingAttempt attempt = ReleaseSchedule.Plan(
                config,
                new DateTime(2026, 11, 10, 12, 0, 0));

            Equal("Law Library Study Tables", config.AreaName, "law library area");
            Equal(new DateTime(2026, 11, 11), attempt.TargetDate, "next-day target");
            Equal("13:00", attempt.Slot.StartText, "law library second slot start");
            Equal("18:00", attempt.Slot.EndText, "law library second slot end");

            config.TimeSlotIndex = 0;
            BookingSlot first = BookingSlots.Resolve(config, attempt.TargetDate);
            Equal("09:00", first.StartText, "law library first slot start");
            Equal("13:00", first.EndText, "law library first slot end");

            config.TimeSlotIndex = 2;
            BookingSlot third = BookingSlots.Resolve(config, attempt.TargetDate);
            Equal("18:00", third.StartText, "law library third slot start");
            Equal("21:00", third.EndText, "law library third slot end");
        }

        private static void TestChiWahHourlySlots()
        {
            BookingConfig config = BookingConfig.CreateDefault();
            config.AreaName = "Chi Wah Learning Commons Study Rooms";
            config.TimeSlotIndex = 11;
            BookingSlot slot = BookingSlots.Resolve(config, new DateTime(2026, 11, 13));
            Equal("13:00", slot.StartText, "chi wah hourly slot start");
            Equal("14:00", slot.EndText, "chi wah hourly slot end");

            string[] names = BookingSlots.NamesForArea(config.AreaName);
            Equal(23, names.Length, "chi wah preset count including custom");
            True(BookingSlots.IsCustom(config.AreaName, 22), "chi wah custom index");
            False(BookingSlots.IsCustom("Law Library Study Tables", 2), "law library third slot is preset");
        }

        private static void TestReleaseGrace()
        {
            BookingConfig config = BookingConfig.CreateDefault();
            config.ReleaseTime = "00:00";
            BookingAttempt attempt = ReleaseSchedule.Plan(
                config,
                new DateTime(2026, 11, 9, 0, 1, 20));
            Equal(new DateTime(2026, 11, 9, 0, 0, 0), attempt.ReleaseAt, "release inside grace window");
        }

        private static void TestCountdownFormat()
        {
            Equal("01:02:03", ReleaseSchedule.FormatCountdown(new TimeSpan(1, 2, 3)), "same-day countdown");
            Equal("2天 03:04:05", ReleaseSchedule.FormatCountdown(new TimeSpan(2, 3, 4, 5)), "multi-day countdown");
            Equal("00:00:00", ReleaseSchedule.FormatCountdown(TimeSpan.FromSeconds(-4)), "negative countdown");
        }

        private static void TestFormDetection()
        {
            const string html = @"
<html><body>
<form method='post' action='/book/submit'>
<input type='hidden' name='__VIEWSTATE' value='state &amp; more'>
<input type='hidden' name='__EVENTVALIDATION' value='validation'>
<input type='radio' name='seatChoice' value='A-17' id='seatA17' checked>
<input type='radio' name='slotChoice' value='08:30-13:00' id='slot0830'>
<button type='submit' name='bookButton' value='Book'>Confirm booking</button>
</form>
</body></html>";
            BookingConfig config = BookingConfig.CreateDefault();
            config.SeatLabel = "A-17";
            BookingSlot slot = BookingSlots.Create("第 1 时段", "08:30", "13:00");
            FormChoice choice = WebFormParser.BuildBookingChoice(html, config, slot);
            NotNull(choice, "form choice");
            Equal("/book/submit", choice.Form.Action, "form action");
            Equal("state & more", choice.Fields["__VIEWSTATE"], "decoded hidden state");
            Equal("A-17", choice.Fields["seatChoice"], "seat selected");
            Equal("08:30-13:00", choice.Fields["slotChoice"], "slot selected");
            Equal("Book", choice.Fields["bookButton"], "submit button");
        }

        private static void TestExplicitOverrides()
        {
            const string html = @"
<form method='post' action='/confirm'>
<input type='hidden' name='__VIEWSTATE' value='v'>
<input type='text' name='seatId' value=''>
<input type='hidden' name='period' value=''>
<input type='submit' name='submitButton' value='Go'>
</form>";
            BookingConfig config = BookingConfig.CreateDefault();
            config.SeatFieldName = "seatId";
            config.SeatFieldValue = "B-22";
            config.SlotFieldName = "period";
            config.SlotFieldValue = "SLOT-2";
            config.SubmitFieldName = "submitButton";
            config.SubmitFieldValue = "CONFIRM";
            FormChoice choice = WebFormParser.BuildBookingChoice(
                html,
                config,
                BookingSlots.Create("第 2 时段", "13:00", "18:00"));
            Equal("B-22", choice.Fields["seatId"], "explicit seat");
            Equal("SLOT-2", choice.Fields["period"], "explicit slot");
            Equal("CONFIRM", choice.Fields["submitButton"], "explicit submit");
        }

        private static void TestLoginForm()
        {
            const string html = @"
<form method='post' action='/login'>
<input type='hidden' name='csrf' value='abc'>
<input type='text' name='username'>
<input type='password' name='password'>
<input type='submit' name='loginButton' value='Sign in'>
</form>";
            BookingConfig config = BookingConfig.CreateDefault();
            FormChoice choice = WebFormParser.BuildLoginChoice(html, config, "u123", "fixture-value");
            NotNull(choice, "login choice");
            Equal("u123", choice.Fields["username"], "login username");
            Equal("fixture-value", choice.Fields["password"], "login field value");
            Equal("abc", choice.Fields["csrf"], "login hidden field");
        }

        private static void TestSelectAndPostback()
        {
            const string html = @"
<form method='post' action='/Secure/FacilityStatusDate.aspx'>
<input type='hidden' name='__VIEWSTATE' value='v'>
<select name='ctl00$main$ddlLibrary'>
<option value='6' selected='selected'>Law Library</option>
<option value='3'>Main Library</option>
</select>
<select name='ctl00$main$ddlView'>
<option value='2026-11-08'>2026-11-08 (Sun)</option>
<option value='2026-11-09'>2026-11-09 (Mon)</option>
</select>
<input type='submit' name='ctl00$main$btnGetResult' value='Search'>
<a href=""javascript:__doPostBack('ctl00$main$slot','08:30|13:00')"">08:30 - 13:00</a>
</form>";
            IList<HtmlForm> forms = WebFormParser.ParseForms(html);
            Equal(1, forms.Count, "form count");
            HtmlForm form = forms[0];
            IDictionary<string, string> fields = form.BuildSubmission();
            True(
                WebFormParser.SetSelectByValue(form, fields, "ddlLibrary", "3"),
                "library select");
            Equal("3", fields["ctl00$main$ddlLibrary"], "library value");
            True(
                WebFormParser.SetSelectByValue(form, fields, "ddlView", "2026-11-09"),
                "date select");
            Equal("2026-11-09", fields["ctl00$main$ddlView"], "date value");
            HtmlControl link = null;
            foreach (HtmlControl control in form.Controls)
            {
                if (control.Tag == "a")
                {
                    link = control;
                    break;
                }
            }
            NotNull(link, "postback link");
            Equal("ctl00$main$slot", link.EventTarget, "postback target");
            Equal("08:30|13:00", link.EventArgument, "postback argument");
            BookingConfig config = BookingConfig.CreateDefault();
            FormChoice choice = WebFormParser.BuildBookingChoice(
                html,
                config,
                BookingSlots.Create("第 1 时段", "08:30", "13:00"));
            True(choice.SeatControl == null, "facility-status selects are not seat controls");
        }

        private static void TestConfigNormalization()
        {
            BookingConfig config = new BookingConfig
            {
                MaxRetries = 999,
                RetryIntervalMs = 1,
                OpacityPercent = 3,
                TargetDayOffset = 99,
                TimeSlotIndex = -1,
                ReleaseTime = "99:99"
            };
            config.Normalize();
            Equal(50, config.MaxRetries, "retry clamp");
            Equal(100, config.RetryIntervalMs, "interval clamp");
            Equal(35, config.OpacityPercent, "opacity clamp");
            Equal(7, config.TargetDayOffset, "date offset clamp");
            Equal(0, config.TimeSlotIndex, "slot clamp");
            Equal("00:00", config.ReleaseTime, "invalid clock fallback");
        }

        private static void TestBookingConfirmationButton()
        {
            const string html = @"
<form method='post' action='/Secure/NewBooking.aspx?library=5&amp;ftype=29'>
<input type='hidden' name='__VIEWSTATE' value='state'>
<input type='hidden' name='__EVENTVALIDATION' value='validation'>
<input type='submit' name='ctl00$main$btnSubmitYes' value='Yes'>
<input type='submit' name='ctl00$main$btnSubmitNo' value='No'>
</form>";
            FormChoice choice = WebFormParser.BuildNamedSubmitChoice(html, "btnSubmitYes");
            NotNull(choice, "confirmation choice");
            Equal("Yes", choice.Fields["ctl00$main$btnSubmitYes"], "confirmation button value");
            True(
                !choice.Fields.ContainsKey("ctl00$main$btnSubmitNo"),
                "negative confirmation button must not be submitted");
        }

        private static void TestConfigPersistence()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hku-seat-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(directory, "settings.json");
                ConfigStore store = new ConfigStore(path, new TestProtector());
                BookingConfig config = BookingConfig.CreateDefault();
                config.SetupCompleted = true;
                config.Username = "student";
                store.SetCookie(config, "FIXTURE_SENSITIVE_VALUE_A");
                store.SetPassword(config, "FIXTURE_SENSITIVE_VALUE_B");
                store.Save(config);

                string json = File.ReadAllText(path);
                False(json.Contains("FIXTURE_SENSITIVE_VALUE_A"), "stored value A must not be plaintext");
                False(json.Contains("FIXTURE_SENSITIVE_VALUE_B"), "stored value B must not be plaintext");
                BookingConfig loaded = store.Load();
                NotNull(loaded, "loaded config");
                Equal("FIXTURE_SENSITIVE_VALUE_A", store.ReadCookie(loaded), "sensitive value A round trip");
                Equal("FIXTURE_SENSITIVE_VALUE_B", store.ReadPassword(loaded), "sensitive value B round trip");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void TestTargetSummary()
        {
            BookingConfig config = BookingConfig.CreateDefault();
            config.AreaName = "Main Library 4/F Study Tables";
            config.SeatLabel = "A-17";
            BookingAttempt attempt = ReleaseSchedule.Plan(config, new DateTime(2026, 11, 8, 12, 0, 0));
            string text = ReleaseSchedule.FormatTarget(attempt, config.AreaName, config.SeatLabel);
            True(text.Contains("Main Library"), "area in target");
            True(text.Contains("A-17"), "seat in target");
            True(text.Contains("08:30"), "slot in target");
        }

        private static void TestServerClockCalibration()
        {
            DateTime localStart = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
            ServerClockEstimate estimate = ServerClockCalibration.Estimate(new[]
            {
                new ServerClockSample
                {
                    RequestStartedUtc = localStart,
                    ResponseReceivedUtc = localStart.AddMilliseconds(200),
                    ServerUtc = localStart.AddMilliseconds(2100)
                },
                new ServerClockSample
                {
                    RequestStartedUtc = localStart.AddSeconds(1),
                    ResponseReceivedUtc = localStart.AddMilliseconds(1300),
                    ServerUtc = localStart.AddMilliseconds(3150)
                },
                new ServerClockSample
                {
                    RequestStartedUtc = localStart.AddSeconds(2),
                    ResponseReceivedUtc = localStart.AddMilliseconds(2100),
                    ServerUtc = localStart.AddMilliseconds(4050)
                }
            });

            True(estimate.HasSamples, "clock estimate should have samples");
            Equal(3, estimate.SampleCount, "clock sample count");
            True(
                Math.Abs(estimate.Offset.TotalMilliseconds - 2000) <= 1,
                "median offset should account for half round-trip delay");
            True(
                estimate.MedianRoundTrip >= TimeSpan.FromMilliseconds(100) &&
                estimate.MedianRoundTrip <= TimeSpan.FromMilliseconds(300),
                "median round-trip should be reported");
        }

        private static void TestMultiStepBookingFlow()
        {
            using (MockBookingServer server = new MockBookingServer())
            {
                string baseUrl = server.BaseUrl;
                BookingConfig config = BookingConfig.CreateDefault();
                config.SetupCompleted = true;
                config.AreaName = "Main Library 4/F Study Tables";
                config.BookingPageUrl = baseUrl + "Secure/FacilityStatusDate.aspx";
                config.LoginUrl = baseUrl + "Login.aspx";
                config.SeatLabel = "T-02";
                config.TimeSlotIndex = 0;

                BookingAttempt attempt = new BookingAttempt
                {
                    ReleaseAt = DateTime.Now,
                    TargetDate = new DateTime(2026, 11, 9),
                    Slot = BookingSlots.Create("第 1 时段", "08:30", "13:00")
                };

                using (HkuBookingClient client = new HkuBookingClient(config, string.Empty, string.Empty))
                {
                    PreparedPage page = client.Prepare(config.BookingPageUrl, CancellationToken.None);
                    NotNull(page.ClockSample, "prepared page clock sample");
                    BookingResult result = client.Submit(attempt, CancellationToken.None);
                    True(result.Success, "booking result should be success: " + result.Message);
                }

                True(server.Requests.Count >= 4, "expected multi-step requests");
                True(server.Requests[1].Contains("ddlLibrary=3"), "library selection posted");
                True(
                    server.Requests[1].Contains(Uri.EscapeDataString("2026-11-09")),
                    "target date posted");
                True(
                    server.Requests[2].Contains(Uri.EscapeDataString("ctl00$main$slot")),
                    "slot postback posted");
                True(server.Requests[3].Contains("T-02"), "seat selection posted");
                True(server.Requests[3].Contains("confirmBooking=Book"), "confirmation posted");
            }
        }

        private static void RunTest(string name, Action test)
        {
            try
            {
                test();
                passed++;
            }
            catch (Exception exception)
            {
                failures.Add(name + ": " + exception.Message);
            }
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(
                    description + " expected '" + expected + "' but got '" + actual + "'");
            }
        }

        private static void True(bool condition, string description)
        {
            if (!condition)
            {
                throw new Exception(description + " was false");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new Exception(description + " was true");
            }
        }

        private static void NotNull(object value, string description)
        {
            if (value == null)
            {
                throw new Exception(description + " was null");
            }
        }

        private sealed class TestProtector : ISecretProtector
        {
            private const string Prefix = "test-protected:";

            public string Protect(string plainText)
            {
                if (string.IsNullOrEmpty(plainText))
                {
                    return string.Empty;
                }
                byte[] bytes = Encoding.UTF8.GetBytes(plainText);
                for (int index = 0; index < bytes.Length; index++)
                {
                    bytes[index] ^= 0xA5;
                }
                return Prefix + Convert.ToBase64String(bytes);
            }

            public string Unprotect(string protectedText)
            {
                if (string.IsNullOrEmpty(protectedText))
                {
                    return string.Empty;
                }
                if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    return protectedText;
                }
                byte[] bytes = Convert.FromBase64String(protectedText.Substring(Prefix.Length));
                for (int index = 0; index < bytes.Length; index++)
                {
                    bytes[index] ^= 0xA5;
                }
                return Encoding.UTF8.GetString(bytes);
            }
        }

        private sealed class MockBookingServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Thread thread;
            private readonly int port;

            public MockBookingServer()
            {
                this.listener = new TcpListener(IPAddress.Loopback, 0);
                this.listener.Start();
                this.port = ((IPEndPoint)this.listener.LocalEndpoint).Port;
                this.Requests = new List<string>();
                this.thread = new Thread(this.Listen);
                this.thread.IsBackground = true;
                this.thread.Start();
            }

            public string BaseUrl
            {
                get { return "http://127.0.0.1:" + this.port + "/"; }
            }

            public List<string> Requests { get; private set; }

            public void Dispose()
            {
                try
                {
                    this.listener.Stop();
                }
                catch
                {
                }
            }

            private void Listen()
            {
                try
                {
                    int requestCount = 0;
                    while (requestCount < 8)
                    {
                        using (TcpClient client = this.listener.AcceptTcpClient())
                        using (NetworkStream stream = client.GetStream())
                        {
                            string request = ReadRequest(stream);
                            string body = ExtractBody(request);
                            lock (this.Requests)
                            {
                                this.Requests.Add(body);
                            }

                            requestCount++;
                            string response;
                            if (requestCount == 1)
                            {
                                response = SearchPage("A");
                            }
                            else if (requestCount == 2)
                            {
                                response = SlotPage();
                            }
                            else if (requestCount == 3)
                            {
                                response = SeatPage();
                            }
                            else
                            {
                                response = "<html><body>Your booking is confirmed. Booking successful.</body></html>";
                            }
                            SendResponse(stream, response);
                        }
                    }
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            private static string SearchPage(string state)
            {
                return @"
<html><body><form method='post' action='/Secure/FacilityStatusDate.aspx'>
<input type='hidden' name='__VIEWSTATE' value='" + state + @"'>
<input type='hidden' name='__EVENTVALIDATION' value='validation'>
<select name='ctl00$main$ddlLibrary'>
<option value=''>- Please Select -</option>
<option value='6' selected='selected'>Law Library</option>
<option value='3'>Main Library</option>
</select>
<select name='ctl00$main$ddlType'>
<option value='21'>Discussion Room</option>
<option value='23' selected='selected'>Study Table</option>
</select>
<select name='ctl00$main$ddlView'>
<option value='2026-11-08'>2026-11-08 (Sun)</option>
<option value='2026-11-09'>2026-11-09 (Mon)</option>
</select>
<input type='submit' name='ctl00$main$btnGetResult' value='Search'>
</form></body></html>";
            }

            private static string SlotPage()
            {
                return @"
<html><body><form method='post' action='/Secure/FacilityStatusDate.aspx'>
<input type='hidden' name='__VIEWSTATE' value='B'>
<input type='hidden' name='__EVENTVALIDATION' value='validation-b'>
<a href=""javascript:__doPostBack('ctl00$main$slot','2026-11-09|08:30')"">08:30 - 13:00</a>
</form></body></html>";
            }

            private static string SeatPage()
            {
                return @"
<html><body><form method='post' action='/Secure/NewBooking.aspx'>
<input type='hidden' name='__VIEWSTATE' value='C'>
<input type='hidden' name='__EVENTVALIDATION' value='validation-c'>
<input type='radio' name='ctl00$main$seat' value='T-02' checked='checked'>
<button type='submit' name='confirmBooking' value='Book'>Book</button>
</form></body></html>";
            }

            private static string ReadRequest(NetworkStream stream)
            {
                MemoryStream buffer = new MemoryStream();
                byte[] one = new byte[1];
                while (buffer.Length < 128 * 1024)
                {
                    int read = stream.Read(one, 0, 1);
                    if (read == 0)
                    {
                        break;
                    }
                    buffer.WriteByte(one[0]);
                    string text = Encoding.ASCII.GetString(buffer.ToArray());
                    int headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headerEnd < 0)
                    {
                        continue;
                    }
                    int contentLength = 0;
                    foreach (string line in text.Substring(0, headerEnd).Split(new[] { "\r\n" }, StringSplitOptions.None))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
                        }
                    }
                    if (buffer.Length >= headerEnd + 4 + contentLength)
                    {
                        break;
                    }
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            private static string ExtractBody(string request)
            {
                int separator = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                return separator < 0 ? string.Empty : request.Substring(separator + 4);
            }

            private static void SendResponse(NetworkStream stream, string body)
            {
                byte[] content = Encoding.UTF8.GetBytes(body);
                string headers = "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    "Date: " + DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture) + "\r\n" +
                    "Content-Length: " + content.Length + "\r\n" +
                    "Connection: close\r\n\r\n";
                byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(content, 0, content.Length);
            }
        }
    }
}
