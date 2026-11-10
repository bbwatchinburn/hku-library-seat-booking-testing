using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HkuLibrarySeatBooking
{
    internal sealed class HtmlControl
    {
        public string Tag { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }
        public string Value { get; set; }
        public string Text { get; set; }
        public bool IsChecked { get; set; }
        public bool IsDisabled { get; set; }
        public string EventTarget { get; set; }
        public string EventArgument { get; set; }
        public List<HtmlOption> Options { get; set; }

        public HtmlControl()
        {
            this.Options = new List<HtmlOption>();
        }

        public string SearchText
        {
            get
            {
                return string.Join(
                    " ",
                    new[] { this.Name, this.Id, this.Value, this.Text })
                    .ToLowerInvariant();
            }
        }
    }

    internal sealed class HtmlOption
    {
        public string Value { get; set; }
        public string Text { get; set; }
        public bool Selected { get; set; }
    }

    internal sealed class HtmlForm
    {
        public string Action { get; set; }
        public string Method { get; set; }
        public string RawHtml { get; set; }
        public List<HtmlControl> Controls { get; set; }

        public HtmlForm()
        {
            this.Method = "GET";
            this.Controls = new List<HtmlControl>();
        }

        public HtmlControl FindControl(string name)
        {
            foreach (HtmlControl control in this.Controls)
            {
                if (string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return control;
                }
            }
            return null;
        }

        public IDictionary<string, string> BuildSubmission()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (HtmlControl control in this.Controls)
            {
                if (string.IsNullOrWhiteSpace(control.Name) || control.IsDisabled)
                {
                    continue;
                }

                string type = (control.Type ?? string.Empty).ToLowerInvariant();
                bool include = type == "hidden" ||
                    type == "text" ||
                    type == "email" ||
                    type == "password" ||
                    type == "number" ||
                    type == "date" ||
                    type == "search" ||
                    type == "tel" ||
                    type == "url" ||
                    type == "select" ||
                    ((type == "radio" || type == "checkbox") && control.IsChecked) ||
                    string.IsNullOrEmpty(type);

                if (include)
                {
                    values[control.Name] = Decode(control.Value ?? string.Empty);
                }
            }
            return values;
        }

        private static string Decode(string value)
        {
            return WebUtility.HtmlDecode(value ?? string.Empty);
        }
    }

    internal sealed class FormChoice
    {
        public HtmlForm Form { get; set; }
        public IDictionary<string, string> Fields { get; set; }
        public HtmlControl SeatControl { get; set; }
        public HtmlControl SlotControl { get; set; }
        public HtmlControl SubmitControl { get; set; }
        public string EventTarget { get; set; }
        public string EventArgument { get; set; }
        public string SubmitName { get; set; }
        public string SubmitValue { get; set; }
    }

    internal static class WebFormParser
    {
        private static readonly Regex FormRegex = new Regex(
            @"<form\b(?<attrs>[^>]*)>(?<body>.*?)</form\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex InputRegex = new Regex(
            @"<(?:input|button|textarea)\b(?<attrs>[^>]*?)(?:/>|>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex SelectRegex = new Regex(
            @"<select\b(?<attrs>[^>]*)>(?<body>.*?)</select\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex OptionRegex = new Regex(
            @"<option\b(?<attrs>[^>]*)>(?<text>.*?)</option\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex AnchorRegex = new Regex(
            @"<a\b(?<attrs>[^>]*)>(?<text>.*?)</a\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex PostBackRegex = new Regex(
            @"__doPostBack\(\s*['""](?<target>[^'""]+)['""]\s*,\s*['""](?<argument>[^'""]*)['""]\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IList<HtmlForm> ParseForms(string html)
        {
            List<HtmlForm> forms = new List<HtmlForm>();
            string source = html ?? string.Empty;
            foreach (Match match in FormRegex.Matches(source))
            {
                Dictionary<string, string> attributes = ParseAttributes(match.Groups["attrs"].Value);
                HtmlForm form = new HtmlForm
                {
                    Action = Get(attributes, "action"),
                    Method = ValueOr(Get(attributes, "method"), "GET"),
                    RawHtml = match.Groups["body"].Value
                };

                foreach (Match inputMatch in InputRegex.Matches(form.RawHtml))
                {
                    Dictionary<string, string> inputAttributes =
                        ParseAttributes(inputMatch.Groups["attrs"].Value);
                    string tag = Regex.Match(inputMatch.Value, @"^<\s*(?<tag>\w+)")
                        .Groups["tag"].Value.ToLowerInvariant();
                    string type = Get(inputAttributes, "type");
                    string onclick = Get(inputAttributes, "onclick");
                    Match postBack = PostBackRegex.Match(onclick);
                    if (!postBack.Success)
                    {
                        postBack = PostBackRegex.Match(Get(inputAttributes, "href"));
                    }

                    HtmlControl control = new HtmlControl
                    {
                        Tag = tag,
                        Type = string.IsNullOrWhiteSpace(type) && tag == "button" ? "submit" : type,
                        Name = Decode(Get(inputAttributes, "name")),
                        Id = Decode(Get(inputAttributes, "id")),
                        Value = Decode(Get(inputAttributes, "value")),
                        Text = ExtractButtonText(inputMatch.Value),
                        IsChecked = inputAttributes.ContainsKey("checked"),
                        IsDisabled = inputAttributes.ContainsKey("disabled"),
                        EventTarget = postBack.Success ? Decode(postBack.Groups["target"].Value) : null,
                        EventArgument = postBack.Success ? Decode(postBack.Groups["argument"].Value) : ""
                    };
                    form.Controls.Add(control);
                }

                foreach (Match selectMatch in SelectRegex.Matches(form.RawHtml))
                {
                    Dictionary<string, string> selectAttributes =
                        ParseAttributes(selectMatch.Groups["attrs"].Value);
                    HtmlControl select = new HtmlControl
                    {
                        Tag = "select",
                        Type = "select",
                        Name = Decode(Get(selectAttributes, "name")),
                        Id = Decode(Get(selectAttributes, "id")),
                        IsDisabled = selectAttributes.ContainsKey("disabled")
                    };

                    StringBuilder optionText = new StringBuilder();
                    foreach (Match optionMatch in OptionRegex.Matches(selectMatch.Groups["body"].Value))
                    {
                        Dictionary<string, string> optionAttributes =
                            ParseAttributes(optionMatch.Groups["attrs"].Value);
                        HtmlOption option = new HtmlOption
                        {
                            Value = Decode(Get(optionAttributes, "value")),
                            Text = StripMarkup(Decode(optionMatch.Groups["text"].Value)),
                            Selected = optionAttributes.ContainsKey("selected")
                        };
                        select.Options.Add(option);
                        optionText.Append(' ').Append(option.Text);
                        if (option.Selected)
                        {
                            select.Value = option.Value;
                        }
                    }
                    select.Text = optionText.ToString().Trim();
                    form.Controls.Add(select);
                }

                foreach (Match anchorMatch in AnchorRegex.Matches(form.RawHtml))
                {
                    Dictionary<string, string> anchorAttributes =
                        ParseAttributes(anchorMatch.Groups["attrs"].Value);
                    string onclick = Get(anchorAttributes, "onclick");
                    Match postBack = PostBackRegex.Match(onclick);
                    if (!postBack.Success)
                    {
                        postBack = PostBackRegex.Match(Get(anchorAttributes, "href"));
                    }
                    HtmlControl link = new HtmlControl
                    {
                        Tag = "a",
                        Type = "link",
                        Name = Decode(Get(anchorAttributes, "name")),
                        Id = Decode(Get(anchorAttributes, "id")),
                        Value = Decode(Get(anchorAttributes, "href")),
                        Text = StripMarkup(Decode(anchorMatch.Groups["text"].Value)),
                        EventTarget = postBack.Success ? Decode(postBack.Groups["target"].Value) : null,
                        EventArgument = postBack.Success ? Decode(postBack.Groups["argument"].Value) : ""
                    };
                    if (!string.IsNullOrWhiteSpace(link.Value) ||
                        !string.IsNullOrWhiteSpace(link.EventTarget))
                    {
                        form.Controls.Add(link);
                    }
                }

                forms.Add(form);
            }
            return forms;
        }

        public static FormChoice BuildBookingChoice(string html, BookingConfig config, BookingSlot slot)
        {
            IList<HtmlForm> forms = ParseForms(html);
            HtmlForm selectedForm = null;
            foreach (HtmlForm form in forms)
            {
                if (form.FindControl("__VIEWSTATE") != null ||
                    form.FindControl("__EVENTVALIDATION") != null ||
                    ContainsBookingControl(form, config, slot))
                {
                    selectedForm = form;
                    break;
                }
            }

            if (selectedForm == null && forms.Count > 0)
            {
                selectedForm = forms[0];
            }
            if (selectedForm == null)
            {
                return null;
            }

            IDictionary<string, string> fields = selectedForm.BuildSubmission();
            HtmlControl seatControl = FindSeatControl(selectedForm, config);
            HtmlControl slotControl = FindSlotControl(selectedForm, config, slot);
            HtmlControl submitControl = FindSubmitControl(selectedForm, config);

            if (!string.IsNullOrWhiteSpace(config.SeatFieldName))
            {
                fields[config.SeatFieldName] = config.SeatFieldValue ?? string.Empty;
            }
            else if (seatControl != null && !string.IsNullOrWhiteSpace(seatControl.Name))
            {
                if ((seatControl.Type ?? string.Empty).Equals("select", StringComparison.OrdinalIgnoreCase))
                {
                    string value = FindOptionValue(seatControl, config.SeatLabel);
                    fields[seatControl.Name] = value ?? seatControl.Value ?? string.Empty;
                }
                else
                {
                    fields[seatControl.Name] = seatControl.Value ?? "on";
                }
            }

            if (!string.IsNullOrWhiteSpace(config.SlotFieldName))
            {
                fields[config.SlotFieldName] = config.SlotFieldValue ?? string.Empty;
            }
            else if (slotControl != null && !string.IsNullOrWhiteSpace(slotControl.Name))
            {
                fields[slotControl.Name] = slotControl.Value ?? "on";
            }

            string eventTarget = submitControl == null ? null : submitControl.EventTarget;
            string eventArgument = submitControl == null ? null : submitControl.EventArgument;
            if (submitControl != null && !string.IsNullOrWhiteSpace(submitControl.Name))
            {
                fields[submitControl.Name] = submitControl.Value ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(config.SubmitFieldName))
            {
                fields[config.SubmitFieldName] = config.SubmitFieldValue ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(eventTarget))
            {
                Match postBack = PostBackRegex.Match(html ?? string.Empty);
                if (postBack.Success)
                {
                    eventTarget = Decode(postBack.Groups["target"].Value);
                    eventArgument = Decode(postBack.Groups["argument"].Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(eventTarget) &&
                !fields.ContainsKey("__EVENTTARGET"))
            {
                fields["__EVENTTARGET"] = eventTarget;
                fields["__EVENTARGUMENT"] = eventArgument ?? string.Empty;
            }

            return new FormChoice
            {
                Form = selectedForm,
                Fields = fields,
                SeatControl = seatControl,
                SlotControl = slotControl,
                SubmitControl = submitControl,
                EventTarget = eventTarget,
                EventArgument = eventArgument,
                SubmitName = submitControl == null ? null : submitControl.Name,
                SubmitValue = submitControl == null ? null : submitControl.Value
            };
        }

        public static FormChoice BuildLoginChoice(string html, BookingConfig config, string username, string password)
        {
            foreach (HtmlForm form in ParseForms(html))
            {
                HtmlControl passwordControl = null;
                HtmlControl usernameControl = null;
                foreach (HtmlControl control in form.Controls)
                {
                    string type = (control.Type ?? string.Empty).ToLowerInvariant();
                    if (type == "password" &&
                        (string.IsNullOrWhiteSpace(config.LoginPasswordField) ||
                         string.Equals(control.Name, config.LoginPasswordField, StringComparison.OrdinalIgnoreCase)))
                    {
                        passwordControl = control;
                    }
                    if ((type == "text" || type == "email" || string.IsNullOrEmpty(type)) &&
                        (string.Equals(control.Name, config.LoginUsernameField, StringComparison.OrdinalIgnoreCase) ||
                         control.SearchText.Contains("user") ||
                         control.SearchText.Contains("login") ||
                         control.SearchText.Contains("email")))
                    {
                        usernameControl = control;
                    }
                }

                if (passwordControl == null)
                {
                    continue;
                }

                IDictionary<string, string> fields = form.BuildSubmission();
                if (usernameControl != null && !string.IsNullOrWhiteSpace(usernameControl.Name))
                {
                    fields[usernameControl.Name] = username;
                }

                fields[passwordControl.Name] = password;
                HtmlControl submit = FindFirstSubmit(form);
                if (submit != null && !string.IsNullOrWhiteSpace(submit.Name))
                {
                    fields[submit.Name] = submit.Value ?? string.Empty;
                }

                return new FormChoice
                {
                    Form = form,
                    Fields = fields,
                    EventTarget = submit == null ? null : submit.EventTarget,
                    EventArgument = submit == null ? null : submit.EventArgument,
                    SubmitName = submit == null ? null : submit.Name,
                    SubmitValue = submit == null ? null : submit.Value
                };
            }
            return null;
        }

        public static FormChoice BuildNamedSubmitChoice(string html, string submitNameSuffix)
        {
            foreach (HtmlForm form in ParseForms(html))
            {
                foreach (HtmlControl control in form.Controls)
                {
                    string type = (control.Type ?? string.Empty).ToLowerInvariant();
                    if ((type == "submit" || type == "button") &&
                        (control.Name ?? string.Empty).EndsWith(
                            submitNameSuffix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        IDictionary<string, string> fields = form.BuildSubmission();
                        fields[control.Name] = control.Value ?? string.Empty;
                        return new FormChoice
                        {
                            Form = form,
                            Fields = fields,
                            SubmitControl = control,
                            SubmitName = control.Name,
                            SubmitValue = control.Value
                        };
                    }
                }
            }
            return null;
        }

        public static string EncodeForm(IDictionary<string, string> fields)
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> field in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }
                builder.Append(Uri.EscapeDataString(field.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(field.Value ?? string.Empty));
            }
            return builder.ToString();
        }

        public static string ResolveUrl(string baseUrl, string action)
        {
            Uri actionUri;
            if (Uri.TryCreate(action, UriKind.Absolute, out actionUri))
            {
                return actionUri.ToString();
            }

            Uri baseUri;
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri))
            {
                return new Uri(baseUri, action ?? string.Empty).ToString();
            }
            return baseUrl;
        }

        public static string NormalizeToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            foreach (char character in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }
            return builder.ToString();
        }

        public static HtmlControl FindSelectBySuffix(HtmlForm form, string suffix)
        {
            foreach (HtmlControl control in form.Controls)
            {
                if ((control.Type ?? string.Empty).Equals("select", StringComparison.OrdinalIgnoreCase) &&
                    ((control.Name ?? string.Empty).EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                     (control.Id ?? string.Empty).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                {
                    return control;
                }
            }
            return null;
        }

        public static bool SetSelectByValue(
            HtmlForm form,
            IDictionary<string, string> fields,
            string suffix,
            string value)
        {
            HtmlControl select = FindSelectBySuffix(form, suffix);
            if (select == null)
            {
                return false;
            }
            foreach (HtmlOption option in select.Options)
            {
                if (string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    fields[select.Name] = option.Value;
                    return true;
                }
            }
            return false;
        }

        public static bool SetSelectByText(
            HtmlForm form,
            IDictionary<string, string> fields,
            string suffix,
            string textContains)
        {
            HtmlControl select = FindSelectBySuffix(form, suffix);
            if (select == null)
            {
                return false;
            }
            string needle = NormalizeToken(textContains);
            foreach (HtmlOption option in select.Options)
            {
                string candidate = NormalizeToken(option.Text);
                if (candidate.Contains(needle))
                {
                    fields[select.Name] = option.Value;
                    return true;
                }
            }
            return false;
        }

        private static string FindOptionValue(HtmlControl select, string textContains)
        {
            string needle = NormalizeToken(textContains);
            foreach (HtmlOption option in select.Options)
            {
                if (string.IsNullOrWhiteSpace(option.Value))
                {
                    continue;
                }
                if (needle.Length == 0 || NormalizeToken(option.Text).Contains(needle))
                {
                    return option.Value;
                }
            }
            return null;
        }

        private static bool ContainsBookingControl(HtmlForm form, BookingConfig config, BookingSlot slot)
        {
            return FindSeatControl(form, config) != null ||
                FindSlotControl(form, config, slot) != null;
        }

        private static HtmlControl FindSeatControl(HtmlForm form, BookingConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.SeatFieldName))
            {
                HtmlControl explicitControl = form.FindControl(config.SeatFieldName);
                if (explicitControl != null)
                {
                    return explicitControl;
                }
            }

            string seatToken = NormalizeToken(config.SeatLabel);
            foreach (HtmlControl control in form.Controls)
            {
                string type = (control.Type ?? string.Empty).ToLowerInvariant();
                if (control.IsDisabled ||
                    (type != "radio" && type != "checkbox" && type != "button" &&
                     type != "submit" && type != "link" && type != "select"))
                {
                    continue;
                }

                string candidate = NormalizeToken(
                    string.Join(" ", new[] { control.Name, control.Id, control.Value, control.Text }));
                if (seatToken.Length > 0 && candidate.Contains(seatToken))
                {
                    return control;
                }
                if (seatToken.Length == 0 &&
                    ((control.Name ?? string.Empty).IndexOf("seat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (control.Id ?? string.Empty).IndexOf("seat", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (type == "radio" || type == "checkbox" || type == "select"))
                {
                    return control;
                }
            }
            return null;
        }

        private static HtmlControl FindSlotControl(HtmlForm form, BookingConfig config, BookingSlot slot)
        {
            if (!string.IsNullOrWhiteSpace(config.SlotFieldName))
            {
                HtmlControl explicitControl = form.FindControl(config.SlotFieldName);
                if (explicitControl != null)
                {
                    return explicitControl;
                }
            }

            string startToken = NormalizeToken(slot.StartText);
            string endToken = NormalizeToken(slot.EndText);
            foreach (HtmlControl control in form.Controls)
            {
                string type = (control.Type ?? string.Empty).ToLowerInvariant();
                if (control.IsDisabled ||
                    (type != "radio" && type != "checkbox" && type != "button" &&
                     type != "submit" && type != "link"))
                {
                    continue;
                }

                string candidate = NormalizeToken(
                    string.Join(" ", new[] { control.Name, control.Id, control.Value, control.Text }));
                bool matchesTime = candidate.Contains(startToken) || candidate.Contains(endToken);
                bool matchesSlotName = candidate.Contains(NormalizeToken(slot.Name));
                if (matchesTime || matchesSlotName)
                {
                    return control;
                }
            }
            return null;
        }

        private static HtmlControl FindSubmitControl(HtmlForm form, BookingConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.SubmitFieldName))
            {
                HtmlControl explicitControl = form.FindControl(config.SubmitFieldName);
                if (explicitControl != null)
                {
                    return explicitControl;
                }
            }

            foreach (HtmlControl control in form.Controls)
            {
                string type = (control.Type ?? string.Empty).ToLowerInvariant();
                if ((type == "link" && string.IsNullOrWhiteSpace(control.EventTarget)) ||
                    IsNavigationOrSearch(control))
                {
                    continue;
                }
                if ((type == "submit" || type == "button" || type == "link") &&
                    (control.SearchText.Contains("book") ||
                     control.SearchText.Contains("reserve") ||
                     control.SearchText.Contains("confirm") ||
                     control.SearchText.Contains("submit") ||
                     control.SearchText.Contains("預約") ||
                     control.SearchText.Contains("预约")))
                {
                    return control;
                }
            }
            return FindFirstSubmit(form);
        }

        private static HtmlControl FindFirstSubmit(HtmlForm form)
        {
            foreach (HtmlControl control in form.Controls)
            {
                string type = (control.Type ?? string.Empty).ToLowerInvariant();
                if (IsNavigationOrSearch(control) ||
                    (type == "link" && string.IsNullOrWhiteSpace(control.EventTarget)))
                {
                    continue;
                }
                if (type == "submit" || type == "button" || !string.IsNullOrWhiteSpace(control.EventTarget))
                {
                    return control;
                }
            }
            return null;
        }

        private static bool IsNavigationOrSearch(HtmlControl control)
        {
            string text = control.SearchText;
            return text.Contains("search") ||
                text.Contains("new booking") ||
                text.Contains("my booking") ||
                text.Contains("logout") ||
                text.Contains("view by dates") ||
                text.Contains("view by facility") ||
                text.Contains("advanced search") ||
                text.Contains("please select");
        }

        private static Dictionary<string, string> ParseAttributes(string attributes)
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Regex regex = new Regex(
                @"(?<name>[A-Za-z_:][A-Za-z0-9_.:-]*)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+))",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            foreach (Match match in regex.Matches(attributes ?? string.Empty))
            {
                string value = match.Groups["double"].Success
                    ? match.Groups["double"].Value
                    : match.Groups["single"].Success
                        ? match.Groups["single"].Value
                        : match.Groups["bare"].Value;
                result[match.Groups["name"].Value] = Decode(value);
            }

            foreach (string flag in new[] { "checked", "disabled", "selected", "multiple" })
            {
                if (Regex.IsMatch(attributes ?? string.Empty, @"\b" + flag + @"\b", RegexOptions.IgnoreCase))
                {
                    result[flag] = flag;
                }
            }
            return result;
        }

        private static string ExtractButtonText(string markup)
        {
            Match match = Regex.Match(
                markup ?? string.Empty,
                @">(?<text>[^<]*)<\s*/\s*(?:button|select|textarea)\s*>",
                RegexOptions.IgnoreCase);
            return match.Success ? Decode(match.Groups["text"].Value.Trim()) : string.Empty;
        }

        private static string Get(IDictionary<string, string> values, string name)
        {
            string value;
            return values.TryGetValue(name, out value) ? value : string.Empty;
        }

        private static string Decode(string value)
        {
            return WebUtility.HtmlDecode(value ?? string.Empty);
        }

        private static string StripMarkup(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"<[^>]+>", " ").Trim();
        }

        private static string ValueOr(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
