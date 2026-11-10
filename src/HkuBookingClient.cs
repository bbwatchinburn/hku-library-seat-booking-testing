using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HkuLibrarySeatBooking
{
    internal sealed class ServerClockSample
    {
        public DateTime ServerUtc { get; set; }
        public DateTime RequestStartedUtc { get; set; }
        public DateTime ResponseReceivedUtc { get; set; }

        public TimeSpan RoundTrip
        {
            get { return this.ResponseReceivedUtc - this.RequestStartedUtc; }
        }

        // The server clock is sampled somewhere inside the request/response
        // interval. Subtracting half the round trip removes the expected
        // network delay from the clock offset estimate.
        public TimeSpan Offset
        {
            get
            {
                return this.ServerUtc.Subtract(this.RequestStartedUtc).Subtract(
                    TimeSpan.FromTicks(this.RoundTrip.Ticks / 2));
            }
        }
    }

    internal sealed class ServerClockEstimate
    {
        public bool HasSamples { get; set; }
        public TimeSpan Offset { get; set; }
        public TimeSpan MedianRoundTrip { get; set; }
        public int SampleCount { get; set; }
    }

    internal static class ServerClockCalibration
    {
        public static ServerClockEstimate Estimate(IEnumerable<ServerClockSample> samples)
        {
            List<ServerClockSample> validSamples = new List<ServerClockSample>();
            if (samples != null)
            {
                foreach (ServerClockSample sample in samples)
                {
                    if (sample == null || sample.ServerUtc.Year < 2000)
                    {
                        continue;
                    }

                    TimeSpan roundTrip = sample.RoundTrip;
                    if (roundTrip < TimeSpan.Zero || roundTrip > TimeSpan.FromSeconds(30))
                    {
                        continue;
                    }
                    validSamples.Add(sample);
                }
            }

            if (validSamples.Count == 0)
            {
                return new ServerClockEstimate();
            }

            List<TimeSpan> offsets = new List<TimeSpan>();
            List<TimeSpan> roundTrips = new List<TimeSpan>();
            foreach (ServerClockSample sample in validSamples)
            {
                offsets.Add(sample.Offset);
                roundTrips.Add(sample.RoundTrip);
            }
            offsets.Sort();
            roundTrips.Sort();

            return new ServerClockEstimate
            {
                HasSamples = true,
                Offset = Median(offsets),
                MedianRoundTrip = Median(roundTrips),
                SampleCount = validSamples.Count
            };
        }

        private static TimeSpan Median(IList<TimeSpan> values)
        {
            int middle = values.Count / 2;
            if (values.Count % 2 == 1)
            {
                return values[middle];
            }

            return TimeSpan.FromTicks((values[middle - 1].Ticks + values[middle].Ticks) / 2);
        }
    }

    internal sealed class PreparedPage
    {
        public string Url { get; set; }
        public string Html { get; set; }
        public DateTime? ServerUtc { get; set; }
        public ServerClockSample ClockSample { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public int StatusCode { get; set; }
    }

    internal sealed class HttpResponseData
    {
        public string Url { get; set; }
        public int StatusCode { get; set; }
        public string Content { get; set; }
        public string ContentType { get; set; }
        public DateTime? ServerUtc { get; set; }
        public ServerClockSample ClockSample { get; set; }
    }

    internal sealed class HkuBookingClient : IDisposable
    {
        private const string DefaultUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0";

        private readonly BookingConfig config;
        private readonly string password;
        private readonly CookieContainer cookies;
        private readonly BookingHarRecorder recorder;
        private PreparedPage preparedPage;

        public HkuBookingClient(BookingConfig config, string password, string cookieHeader)
            : this(config, password, cookieHeader, null)
        {
        }

        public HkuBookingClient(
            BookingConfig config,
            string password,
            string cookieHeader,
            BookingHarRecorder recorder)
        {
            this.config = config;
            this.password = password ?? string.Empty;
            this.recorder = recorder;
            this.cookies = new CookieContainer();
            this.LoadCookieHeader(cookieHeader);
        }

        public string LastResponse
        {
            get;
            private set;
        }

        public PreparedPage Prepare(string areaUrl, CancellationToken cancellationToken)
        {
            HttpResponseData response = this.Get(areaUrl, 15000, cancellationToken);
            response = this.FollowMetaRefresh(response, cancellationToken);
            this.LastResponse = response.Content;
            if (response.StatusCode == 401 || response.StatusCode == 403)
            {
                throw new BookingAuthenticationException(
                    "港大预约页拒绝访问（HTTP " + response.StatusCode + "）。请更新 Cookie，或连接校园网/VPN 后重试。");
            }
            if (response.StatusCode >= 400)
            {
                throw new BookingRequestException(
                    "预约页面返回 HTTP " + response.StatusCode + "。",
                    true,
                    response.Content);
            }

            if (LooksLikeLoginPage(response.Content))
            {
                if (this.config.UseCookieOnly)
                {
                    throw new BookingAuthenticationException(
                        "当前 Cookie 已失效或权限不足，请在 Edge 登录港大预约系统后更新 Cookie。");
                }

                response = this.LoginWithForm(cancellationToken);
                response = this.Get(areaUrl, 15000, cancellationToken);
                response = this.FollowMetaRefresh(response, cancellationToken);
                this.LastResponse = response.Content;
            }

            if (LooksLikeLoginPage(response.Content))
            {
                throw new BookingAuthenticationException(
                    "港大预约系统仍要求登录。该站点可能使用 HKU 单点登录，建议使用 Cookie 模式。");
            }

            this.preparedPage = new PreparedPage
            {
                Url = response.Url,
                Html = response.Content,
                ServerUtc = response.ServerUtc,
                ClockSample = response.ClockSample,
                CapturedAtUtc = DateTime.UtcNow,
                StatusCode = response.StatusCode
            };
            return this.preparedPage;
        }

        public ServerClockSample SampleServerClock(
            string areaUrl,
            CancellationToken cancellationToken)
        {
            HttpResponseData response = this.GetWithMethod(
                areaUrl,
                "HEAD",
                15000,
                cancellationToken);
            return response.ClockSample;
        }

        public BookingResult Submit(BookingAttempt attempt, CancellationToken cancellationToken)
        {
            try
            {
                if (this.preparedPage == null)
                {
                    this.Prepare(this.config.BookingPageUrl, cancellationToken);
                }

                if (DateTime.UtcNow - this.preparedPage.CapturedAtUtc > TimeSpan.FromSeconds(30) ||
                    !this.HasTargetDateOption(attempt))
                {
                    this.Prepare(this.config.BookingPageUrl, cancellationToken);
                }

                HttpResponseData response = null;
                bool searched = false;
                bool slotActivated = false;
                for (int navigationStep = 0; navigationStep < 5; navigationStep++)
                {
                    if (!searched && this.TrySearchTarget(attempt, cancellationToken, out response))
                    {
                        searched = true;
                        this.UpdatePreparedPage(response);
                        continue;
                    }

                    FormChoice choice = WebFormParser.BuildBookingChoice(
                        this.preparedPage.Html,
                        this.config,
                        attempt.Slot);
                    if (choice == null)
                    {
                        return BookingResult.Fail(
                            "预约页面中没有找到可提交的表单。请确认馆区 URL，或刷新 Cookie 后重试。",
                            true,
                            false);
                    }

                    if (!slotActivated &&
                        choice.SlotControl != null &&
                        choice.SeatControl == null &&
                        !IsSelectableControl(choice.SlotControl))
                    {
                        response = this.ActivateControl(choice.Form, choice.Fields, choice.SlotControl, cancellationToken);
                        slotActivated = true;
                        if (response != null)
                        {
                            this.UpdatePreparedPage(response);
                            continue;
                        }
                    }

                    if (choice.SeatControl == null &&
                        !IsSelectableControl(choice.SlotControl) &&
                        string.IsNullOrWhiteSpace(this.config.SeatFieldName))
                    {
                        return BookingResult.Fail(
                            "目标日期或时段暂未开放，未找到可选座位。",
                            true,
                            false);
                    }

                    response = this.SubmitChoice(choice, 12000, cancellationToken);
                    this.LastResponse = response.Content;
                    this.UpdatePreparedPage(response);

                    FormChoice confirmation = WebFormParser.BuildNamedSubmitChoice(
                        this.preparedPage.Html,
                        "btnSubmitYes");
                    if (confirmation != null)
                    {
                        response = this.SubmitChoice(confirmation, 12000, cancellationToken);
                        this.LastResponse = response.Content;
                        this.UpdatePreparedPage(response);
                    }
                    return InterpretBookingResponse(response);
                }

                return BookingResult.Fail(
                    "预约流程步骤过多，已停止本次提交以避免误操作。",
                    true,
                    false);
            }
            catch (BookingAuthenticationException exception)
            {
                return BookingResult.Fail(exception.Message, false, true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (BookingRequestException exception)
            {
                return BookingResult.Fail(exception.Message, exception.Retryable, false);
            }
            catch (Exception exception)
            {
                return BookingResult.Fail(
                    "提交预约时发生网络错误：" + exception.Message,
                    true,
                    false,
                    true);
            }
        }

        public void Dispose()
        {
            this.preparedPage = null;
            if (this.recorder != null)
            {
                this.recorder.Save();
            }
        }

        private void UpdatePreparedPage(HttpResponseData response)
        {
            this.LastResponse = response.Content;
            this.preparedPage = new PreparedPage
            {
                Url = response.Url,
                Html = response.Content,
                ServerUtc = response.ServerUtc,
                ClockSample = response.ClockSample,
                CapturedAtUtc = DateTime.UtcNow,
                StatusCode = response.StatusCode
            };
        }

        private bool HasTargetDateOption(BookingAttempt attempt)
        {
            string targetDate = attempt.TargetDate.ToString("yyyy-MM-dd");
            foreach (HtmlForm form in WebFormParser.ParseForms(this.preparedPage.Html))
            {
                HtmlControl dateSelect = WebFormParser.FindSelectBySuffix(form, "ddlView");
                if (dateSelect == null)
                {
                    continue;
                }

                foreach (HtmlOption option in dateSelect.Options)
                {
                    if (string.Equals(option.Value, targetDate, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TrySearchTarget(
            BookingAttempt attempt,
            CancellationToken cancellationToken,
            out HttpResponseData response)
        {
            response = null;
            IList<HtmlForm> forms = WebFormParser.ParseForms(this.preparedPage.Html);
            foreach (HtmlForm form in forms)
            {
                HtmlControl dateSelect = WebFormParser.FindSelectBySuffix(form, "ddlView");
                if (dateSelect == null)
                {
                    continue;
                }

                IDictionary<string, string> fields = form.BuildSubmission();
                HtmlControl library = WebFormParser.FindSelectBySuffix(form, "ddlLibrary");
                if (library != null)
                {
                    string libraryValue = this.ResolveLibraryValue(library);
                    if (libraryValue == null)
                    {
                        throw new BookingRequestException(
                            "港大预约页面中没有找到目标馆区选项。",
                            false,
                            this.preparedPage.Html);
                    }
                    fields[library.Name] = libraryValue;
                }

                HtmlControl type = WebFormParser.FindSelectBySuffix(form, "ddlType");
                if (type != null)
                {
                    string typeValue = this.ResolveFacilityTypeValue(type);
                    if (typeValue == null)
                    {
                        throw new BookingRequestException(
                            "港大预约页面中没有找到“Study Table”设施类型。",
                            false,
                            this.preparedPage.Html);
                    }
                    fields[type.Name] = typeValue;
                }

                string targetDateText = attempt.TargetDate.ToString("yyyy-MM-dd");
                bool dateFound = false;
                foreach (HtmlOption option in dateSelect.Options)
                {
                    if (string.Equals(option.Value, targetDateText, StringComparison.OrdinalIgnoreCase))
                    {
                        fields[dateSelect.Name] = option.Value;
                        dateFound = true;
                        break;
                    }
                }
                if (!dateFound)
                {
                    throw new BookingRequestException(
                        "目标日期 " + targetDateText + " 尚未出现在预约页面，请确认放号时间。",
                        true,
                        this.preparedPage.Html);
                }

                HtmlControl search = null;
                foreach (HtmlControl control in form.Controls)
                {
                    if ((control.Type ?? string.Empty).Equals("submit", StringComparison.OrdinalIgnoreCase) &&
                        ((control.Name ?? string.Empty).EndsWith("btnGetResult", StringComparison.OrdinalIgnoreCase) ||
                         (control.Name ?? string.Empty).IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (control.Text ?? string.Empty).IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (control.Value ?? string.Empty).IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        search = control;
                        break;
                    }
                }
                if (search == null)
                {
                    continue;
                }

                fields[search.Name] = search.Value ?? "Search";
                string action = WebFormParser.ResolveUrl(this.preparedPage.Url, form.Action);
                response = this.PostForm(action, fields, 12000, cancellationToken);
                return true;
            }
            return false;
        }

        private string ResolveLibraryValue(HtmlControl select)
        {
            string code = GetQueryValue(this.config.BookingPageUrl, "lib");
            string requested = null;
            if (!string.IsNullOrWhiteSpace(code))
            {
                requested = code;
            }
            else if (this.config.AreaName.IndexOf("Main Library", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                requested = "3";
            }
            else if (this.config.AreaName.IndexOf("Law", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                requested = "6";
            }
            else if (this.config.AreaName.IndexOf("Chi Wah", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                requested = "5";
            }
            else if (this.config.AreaName.IndexOf("Medical", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                requested = "8";
            }
            else if (this.config.AreaName.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                requested = "4";
            }
            else if (this.config.AreaName.IndexOf("Dental", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                requested = "9";
            }

            if (requested != null)
            {
                foreach (HtmlOption option in select.Options)
                {
                    if (string.Equals(option.Value, requested, StringComparison.OrdinalIgnoreCase))
                    {
                        return option.Value;
                    }
                }
            }
            return null;
        }

        private string ResolveFacilityTypeValue(HtmlControl select)
        {
            string requested = GetQueryValue(this.config.BookingPageUrl, "ftype");
            if (string.IsNullOrWhiteSpace(requested))
            {
                if (this.config.AreaName.IndexOf("Chi Wah", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    this.config.AreaName.IndexOf("Study Room", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    requested = "29";
                }
                else
                {
                    requested = this.config.AreaName.IndexOf("Discussion Room", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "21"
                        : "23";
                }
            }
            foreach (HtmlOption option in select.Options)
            {
                if (string.Equals(option.Value, requested, StringComparison.OrdinalIgnoreCase))
                {
                    return option.Value;
                }
            }
            return null;
        }

        private HttpResponseData ActivateControl(
            HtmlForm form,
            IDictionary<string, string> fields,
            HtmlControl control,
            CancellationToken cancellationToken)
        {
            string type = (control.Type ?? string.Empty).ToLowerInvariant();
            if (type == "link" &&
                string.IsNullOrWhiteSpace(control.EventTarget) &&
                !string.IsNullOrWhiteSpace(control.Value) &&
                !control.Value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) &&
                control.Value != "#")
            {
                return this.Get(
                    WebFormParser.ResolveUrl(this.preparedPage.Url, control.Value),
                    12000,
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(control.EventTarget))
            {
                fields["__EVENTTARGET"] = control.EventTarget;
                fields["__EVENTARGUMENT"] = control.EventArgument ?? string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(control.Name))
            {
                fields[control.Name] = control.Value ?? string.Empty;
            }

            string action = WebFormParser.ResolveUrl(this.preparedPage.Url, form.Action);
            return this.PostForm(action, fields, 12000, cancellationToken);
        }

        private HttpResponseData SubmitChoice(
            FormChoice choice,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            string actionUrl = WebFormParser.ResolveUrl(this.preparedPage.Url, choice.Form.Action);
            string method = string.IsNullOrWhiteSpace(choice.Form.Method)
                ? "POST"
                : choice.Form.Method.ToUpperInvariant();
            if (method == "GET")
            {
                string query = WebFormParser.EncodeForm(choice.Fields);
                string separator = actionUrl.IndexOf('?') >= 0 ? "&" : "?";
                return this.Get(actionUrl + separator + query, timeoutMs, cancellationToken);
            }
            return this.PostForm(actionUrl, choice.Fields, timeoutMs, cancellationToken);
        }

        private static string GetQueryValue(string url, string name)
        {
            Uri uri;
            if (!Uri.TryCreate(url ?? string.Empty, UriKind.Absolute, out uri))
            {
                return null;
            }
            string query = uri.Query.TrimStart('?');
            foreach (string pair in query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }
                if (string.Equals(
                    Uri.UnescapeDataString(pair.Substring(0, equals)),
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair.Substring(equals + 1));
                }
            }
            return null;
        }

        private HttpResponseData LoginWithForm(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(this.config.Username) || string.IsNullOrEmpty(this.password))
            {
                throw new BookingAuthenticationException(
                    "Cookie 已失效，且没有可用的账号密码。请打开设置更新 Cookie。");
            }

            HttpResponseData loginPage = this.Get(this.config.LoginUrl, 15000, cancellationToken);
            FormChoice login = WebFormParser.BuildLoginChoice(
                loginPage.Content,
                this.config,
                this.config.Username,
                this.password);
            if (login == null)
            {
                throw new BookingAuthenticationException(
                    "港大预约系统使用单点登录，应用无法代替浏览器完成统一身份认证，请使用 Cookie 模式。");
            }

            string action = WebFormParser.ResolveUrl(loginPage.Url, login.Form.Action);
            string method = string.IsNullOrWhiteSpace(login.Form.Method)
                ? "POST"
                : login.Form.Method.ToUpperInvariant();
            if (method == "GET")
            {
                string separator = action.IndexOf('?') >= 0 ? "&" : "?";
                return this.Get(
                    action + separator + WebFormParser.EncodeForm(login.Fields),
                    15000,
                    cancellationToken);
            }
            return this.PostForm(action, login.Fields, 15000, cancellationToken);
        }

        private HttpResponseData FollowMetaRefresh(
            HttpResponseData response,
            CancellationToken cancellationToken)
        {
            for (int redirect = 0; redirect < 3; redirect++)
            {
                Match match = Regex.Match(
                    response.Content ?? string.Empty,
                    @"<meta\b[^>]*http-equiv\s*=\s*['""]?refresh['""]?[^>]*content\s*=\s*['""][^'""]*url\s*=\s*(?<url>[^'""]+)",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    break;
                }

                string next = WebFormParser.ResolveUrl(
                    response.Url,
                    WebUtility.HtmlDecode(match.Groups["url"].Value.Trim()));
                response = this.Get(next, 15000, cancellationToken);
            }
            return response;
        }

        private HttpResponseData Get(string url, int timeoutMs, CancellationToken cancellationToken)
        {
            return this.GetWithMethod(url, "GET", timeoutMs, cancellationToken);
        }

        private HttpResponseData GetWithMethod(
            string url,
            string method,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            const int maximumAttempts = 4;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                try
                {
                    return this.Request(
                        url,
                        method,
                        null,
                        null,
                        timeoutMs,
                        cancellationToken,
                        attempt == 1);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (WebException)
                {
                    if (attempt == maximumAttempts)
                    {
                        throw;
                    }

                    int delayMs = attempt == 1 ? 250 : attempt == 2 ? 600 : 1200;
                    if (cancellationToken.WaitHandle.WaitOne(delayMs))
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
            }

            throw new InvalidOperationException("HTTP request retry loop ended unexpectedly.");
        }

        private HttpResponseData PostForm(
            string url,
            IDictionary<string, string> fields,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            return this.Request(
                url,
                "POST",
                Encoding.UTF8.GetBytes(WebFormParser.EncodeForm(fields)),
                "application/x-www-form-urlencoded; charset=UTF-8",
                timeoutMs,
                cancellationToken);
        }

        private HttpResponseData Request(
            string url,
            string method,
            byte[] body,
            string contentType,
            int timeoutMs,
            CancellationToken cancellationToken,
            bool keepAlive = true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new BookingRequestException("馆区 URL 无效。", false, null);
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.CookieContainer = this.cookies;
            request.UserAgent = DefaultUserAgent;
            request.Accept = "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8";
            request.Headers[HttpRequestHeader.AcceptLanguage] = "zh-HK,zh;q=0.9,en;q=0.8";
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.KeepAlive = keepAlive;
            request.Referer = this.config.BookingPageUrl;
            // Let Windows Schannel negotiate the best TLS version supported by
            // both the operating system and the HKU application gateway.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;

            if (body != null)
            {
                request.ContentType = contentType;
                request.ContentLength = body.Length;
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(body, 0, body.Length);
                }
            }

            DateTime requestStartedUtc = DateTime.UtcNow;
            string requestBodyText = body == null ? null : Encoding.UTF8.GetString(body);
            CookieCollection requestCookies = this.cookies.GetCookies(uri);
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    HttpResponseData responseData = ReadResponse(
                        response,
                        requestStartedUtc,
                        DateTime.UtcNow);
                    if (this.recorder != null)
                    {
                        this.recorder.RecordSuccess(
                            method,
                            url,
                            requestStartedUtc,
                            DateTime.UtcNow,
                            request.ContentType,
                            requestBodyText,
                            requestCookies,
                            response,
                            responseData);
                    }
                    return responseData;
                }
            }
            catch (WebException exception)
            {
                DateTime responseReceivedUtc = DateTime.UtcNow;
                HttpWebResponse errorResponse = exception.Response as HttpWebResponse;
                if (errorResponse != null)
                {
                    using (errorResponse)
                    {
                        HttpResponseData errorData = ReadResponse(
                            errorResponse,
                            requestStartedUtc,
                            responseReceivedUtc);
                        if (this.recorder != null)
                        {
                            this.recorder.RecordFailure(
                                method,
                                url,
                                requestStartedUtc,
                                responseReceivedUtc,
                                request.ContentType,
                                requestBodyText,
                                requestCookies,
                                errorResponse,
                                errorData,
                                exception);
                        }
                        return errorData;
                    }
                }
                if (this.recorder != null)
                {
                    this.recorder.RecordFailure(
                        method,
                        url,
                        requestStartedUtc,
                        responseReceivedUtc,
                        request.ContentType,
                        requestBodyText,
                        requestCookies,
                        null,
                        null,
                        exception);
                }
                if (exception.Message.IndexOf("redirection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exception.Message.IndexOf("redirect", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new BookingAuthenticationException(
                        "港大预约系统的登录会话已失效，请重新登录并更新 Cookie。");
                }
                throw;
            }
        }

        private static HttpResponseData ReadResponse(
            HttpWebResponse response,
            DateTime requestStartedUtc,
            DateTime responseReceivedUtc)
        {
            string content;
            Encoding encoding = GetEncoding(response.CharacterSet);
            using (Stream responseStream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(responseStream, encoding, true))
            {
                content = reader.ReadToEnd();
            }

            DateTime serverUtc;
            DateTime? serverTime = null;
            if (DateTime.TryParse(
                response.Headers["Date"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out serverUtc))
            {
                serverTime = serverUtc;
            }

            return new HttpResponseData
            {
                Url = response.ResponseUri == null ? string.Empty : response.ResponseUri.ToString(),
                StatusCode = (int)response.StatusCode,
                ContentType = response.ContentType,
                Content = content,
                ServerUtc = serverTime,
                ClockSample = serverTime.HasValue
                    ? new ServerClockSample
                    {
                        ServerUtc = serverTime.Value,
                        RequestStartedUtc = requestStartedUtc,
                        ResponseReceivedUtc = responseReceivedUtc
                    }
                    : null
            };
        }

        private static Encoding GetEncoding(string charset)
        {
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try
                {
                    return Encoding.GetEncoding(charset.Trim('"'));
                }
                catch
                {
                }
            }
            return Encoding.UTF8;
        }

        private void LoadCookieHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return;
            }

            string value = header.Trim();
            if (value.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring("Cookie:".Length).Trim();
            }

            foreach (string segment in value.Split(';'))
            {
                int equals = segment.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string name = segment.Substring(0, equals).Trim();
                string cookieValue = segment.Substring(equals + 1).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                try
                {
                    this.cookies.Add(new Cookie(name, cookieValue, "/", "booking.lib.hku.hk"));
                }
                catch
                {
                    // Ignore malformed optional cookie attributes.
                }
            }
        }

        private static BookingResult InterpretBookingResponse(HttpResponseData response)
        {
            string text = StripHtml(response.Content ?? string.Empty);
            string lowered = text.ToLowerInvariant();
            bool login = LooksLikeLoginPage(response.Content);
            bool explicitFailure =
                lowered.Contains("not available") ||
                lowered.Contains("already booked") ||
                lowered.Contains("reservation failed") ||
                lowered.Contains("booking failed") ||
                lowered.Contains("failed to book") ||
                lowered.Contains("error") ||
                lowered.Contains("captcha") ||
                text.Contains("已被预约") ||
                text.Contains("预约失败") ||
                text.Contains("預約失敗") ||
                text.Contains("不可預約") ||
                text.Contains("不可预约") ||
                text.Contains("验证码");
            bool success =
                lowered.Contains("booking successful") ||
                lowered.Contains("booked successfully") ||
                lowered.Contains("reservation successful") ||
                lowered.Contains("successfully booked") ||
                lowered.Contains("your booking is confirmed") ||
                lowered.Contains("booking is successful") ||
                text.Contains("預約成功") ||
                text.Contains("预约成功") ||
                text.Contains("成功預約") ||
                text.Contains("成功预约") ||
                (response.Url ?? string.Empty).IndexOf(
                    "MyBookingRecord.aspx",
                    StringComparison.OrdinalIgnoreCase) >= 0;

            if (success && !explicitFailure)
            {
                return BookingResult.Ok("港大预约系统返回预约成功。");
            }
            if (login)
            {
                return BookingResult.Fail(
                    "预约系统要求重新登录，Cookie 已失效。",
                    false,
                    true);
            }
            if (explicitFailure)
            {
                string message = ExtractUsefulMessage(response.Content, text);
                return BookingResult.Fail(
                    string.IsNullOrWhiteSpace(message) ? "港大预约系统返回预约失败。" : message,
                    true,
                    false);
            }

            string fallback = ExtractUsefulMessage(response.Content, text);
            return BookingResult.Fail(
                string.IsNullOrWhiteSpace(fallback)
                    ? "已提交请求，但没有识别到成功信息。请在预约系统核对结果。"
                    : fallback,
                false,
                false);
        }

        private static bool IsSelectableControl(HtmlControl control)
        {
            if (control == null)
            {
                return false;
            }

            string type = (control.Type ?? string.Empty).ToLowerInvariant();
            return type == "checkbox" || type == "radio";
        }

        private static string ExtractUsefulMessage(string html, string text)
        {
            string[] patterns =
            {
                @"class\s*=\s*['""][^'""]*(?:error|alert|message|notification)[^'""]*['""][^>]*>(?<message>.*?)<",
                @"<(?:div|p|span)[^>]*(?:id|class)\s*=\s*['""][^'""]*(?:error|alert|message)[^'""]*['""][^>]*>(?<message>.*?)</",
                @"(?:message|error|alert)\s*[:：]\s*(?<message>[^\r\n<]{3,200})"
            };
            foreach (string pattern in patterns)
            {
                Match match = Regex.Match(
                    html ?? string.Empty,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    string value = StripHtml(match.Groups["message"].Value).Trim();
                    if (value.Length > 0)
                    {
                        return Truncate(value, 240);
                    }
                }
            }
            return Truncate(text.Trim(), 240);
        }

        private static bool LooksLikeLoginPage(string html)
        {
            string value = html ?? string.Empty;
            return Regex.IsMatch(
                value,
                @"<input\b[^>]*\btype\s*=\s*['""]?password",
                RegexOptions.IgnoreCase) ||
                value.IndexOf("Sign in", StringComparison.OrdinalIgnoreCase) >= 0 &&
                value.IndexOf("HKU", StringComparison.OrdinalIgnoreCase) >= 0 &&
                value.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }
            string withoutScripts = Regex.Replace(
                html,
                @"<(script|style)\b[^>]*>.*?</\1\s*>",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string withoutTags = Regex.Replace(withoutScripts, @"<[^>]+>", " ");
            return Regex.Replace(
                WebUtility.HtmlDecode(withoutTags),
                @"\s+",
                " ").Trim();
        }

        private static string Truncate(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length)
            {
                return value ?? string.Empty;
            }
            return value.Substring(0, length) + "…";
        }
    }

    internal sealed class BookingAuthenticationException : Exception
    {
        public BookingAuthenticationException(string message)
            : base(message)
        {
        }
    }

    internal sealed class BookingRequestException : Exception
    {
        public BookingRequestException(string message, bool retryable, string response)
            : base(message)
        {
            this.Retryable = retryable;
            this.Response = response;
        }

        public bool Retryable { get; private set; }
        public string Response { get; private set; }
    }
}
