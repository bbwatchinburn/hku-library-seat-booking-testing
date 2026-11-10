using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace HkuLibrarySeatBooking
{
    [DataContract]
    internal sealed class BookingConfig
    {
        [DataMember(Order = 1)]
        public int Version { get; set; }

        [DataMember(Order = 2)]
        public bool SetupCompleted { get; set; }

        [DataMember(Order = 3)]
        public string Username { get; set; }

        [DataMember(Order = 4)]
        public string ProtectedPassword { get; set; }

        [DataMember(Order = 5)]
        public string ProtectedCookie { get; set; }

        [DataMember(Order = 6)]
        public string AreaName { get; set; }

        [DataMember(Order = 7)]
        public string BookingPageUrl { get; set; }

        [DataMember(Order = 8)]
        public string SeatLabel { get; set; }

        [DataMember(Order = 9)]
        public int TimeSlotIndex { get; set; }

        [DataMember(Order = 10)]
        public string CustomStartTime { get; set; }

        [DataMember(Order = 11)]
        public string CustomEndTime { get; set; }

        [DataMember(Order = 12)]
        public string ReleaseTime { get; set; }

        [DataMember(Order = 13)]
        public int TargetDayOffset { get; set; }

        [DataMember(Order = 14)]
        public int MaxRetries { get; set; }

        [DataMember(Order = 15)]
        public int RetryIntervalMs { get; set; }

        [DataMember(Order = 16)]
        public int OpacityPercent { get; set; }

        [DataMember(Order = 17)]
        public double? WindowLeft { get; set; }

        [DataMember(Order = 18)]
        public double? WindowTop { get; set; }

        [DataMember(Order = 19)]
        public bool UseCookieOnly { get; set; }

        [DataMember(Order = 20)]
        public string LoginUrl { get; set; }

        [DataMember(Order = 21)]
        public string LoginUsernameField { get; set; }

        [DataMember(Order = 22)]
        public string LoginPasswordField { get; set; }

        [DataMember(Order = 23)]
        public string SeatFieldName { get; set; }

        [DataMember(Order = 24)]
        public string SeatFieldValue { get; set; }

        [DataMember(Order = 25)]
        public string SlotFieldName { get; set; }

        [DataMember(Order = 26)]
        public string SlotFieldValue { get; set; }

        [DataMember(Order = 27)]
        public string SubmitFieldName { get; set; }

        [DataMember(Order = 28)]
        public string SubmitFieldValue { get; set; }

        public BookingConfig Clone()
        {
            return new BookingConfig
            {
                Version = this.Version,
                SetupCompleted = this.SetupCompleted,
                Username = this.Username,
                ProtectedPassword = this.ProtectedPassword,
                ProtectedCookie = this.ProtectedCookie,
                AreaName = this.AreaName,
                BookingPageUrl = this.BookingPageUrl,
                SeatLabel = this.SeatLabel,
                TimeSlotIndex = this.TimeSlotIndex,
                CustomStartTime = this.CustomStartTime,
                CustomEndTime = this.CustomEndTime,
                ReleaseTime = this.ReleaseTime,
                TargetDayOffset = this.TargetDayOffset,
                MaxRetries = this.MaxRetries,
                RetryIntervalMs = this.RetryIntervalMs,
                OpacityPercent = this.OpacityPercent,
                WindowLeft = this.WindowLeft,
                WindowTop = this.WindowTop,
                UseCookieOnly = this.UseCookieOnly,
                LoginUrl = this.LoginUrl,
                LoginUsernameField = this.LoginUsernameField,
                LoginPasswordField = this.LoginPasswordField,
                SeatFieldName = this.SeatFieldName,
                SeatFieldValue = this.SeatFieldValue,
                SlotFieldName = this.SlotFieldName,
                SlotFieldValue = this.SlotFieldValue,
                SubmitFieldName = this.SubmitFieldName,
                SubmitFieldValue = this.SubmitFieldValue
            };
        }

        public static BookingConfig CreateDefault()
        {
            return new BookingConfig
            {
                Version = 1,
                SetupCompleted = false,
                AreaName = "Main Library 4/F Study Tables",
                BookingPageUrl = "https://booking.lib.hku.hk/Secure/FacilityStatusDate.aspx",
                SeatLabel = "",
                TimeSlotIndex = 0,
                CustomStartTime = "08:30",
                CustomEndTime = "13:00",
                ReleaseTime = "00:00",
                TargetDayOffset = 1,
                MaxRetries = 5,
                RetryIntervalMs = 800,
                OpacityPercent = 90,
                UseCookieOnly = true,
                LoginUrl = "https://booking.lib.hku.hk/",
                LoginUsernameField = "username",
                LoginPasswordField = "password"
            };
        }

        public void Normalize()
        {
            BookingConfig defaults = CreateDefault();
            this.Version = 1;
            this.AreaName = ValueOr(this.AreaName, defaults.AreaName).Trim();
            this.BookingPageUrl = ValueOr(this.BookingPageUrl, defaults.BookingPageUrl).Trim();
            this.LoginUrl = ValueOr(this.LoginUrl, defaults.LoginUrl).Trim();
            this.Username = (this.Username ?? string.Empty).Trim();
            this.ProtectedPassword = this.ProtectedPassword ?? string.Empty;
            this.ProtectedCookie = this.ProtectedCookie ?? string.Empty;
            this.SeatLabel = (this.SeatLabel ?? string.Empty).Trim();
            this.TimeSlotIndex = Clamp(this.TimeSlotIndex, 0, 31);
            this.CustomStartTime = NormalizeClock(this.CustomStartTime, defaults.CustomStartTime);
            this.CustomEndTime = NormalizeClock(this.CustomEndTime, defaults.CustomEndTime);
            this.ReleaseTime = NormalizeClock(this.ReleaseTime, defaults.ReleaseTime);
            this.TargetDayOffset = Clamp(this.TargetDayOffset, 0, 7);
            this.MaxRetries = Clamp(this.MaxRetries, 0, 50);
            this.RetryIntervalMs = Clamp(this.RetryIntervalMs, 100, 60000);
            this.OpacityPercent = Clamp(this.OpacityPercent, 35, 100);
            this.LoginUsernameField = ValueOr(this.LoginUsernameField, defaults.LoginUsernameField).Trim();
            this.LoginPasswordField = ValueOr(this.LoginPasswordField, defaults.LoginPasswordField).Trim();
            this.SeatFieldName = (this.SeatFieldName ?? string.Empty).Trim();
            this.SeatFieldValue = (this.SeatFieldValue ?? string.Empty).Trim();
            this.SlotFieldName = (this.SlotFieldName ?? string.Empty).Trim();
            this.SlotFieldValue = (this.SlotFieldValue ?? string.Empty).Trim();
            this.SubmitFieldName = (this.SubmitFieldName ?? string.Empty).Trim();
            this.SubmitFieldValue = (this.SubmitFieldValue ?? string.Empty).Trim();
        }

        public DateTime GetNextRelease(DateTime now)
        {
            TimeSpan releaseClock;
            if (!TimeSpan.TryParseExact(this.ReleaseTime, "hh\\:mm", CultureInfo.InvariantCulture, out releaseClock))
            {
                releaseClock = TimeSpan.Zero;
            }

            DateTime hongKongNow = HongKongClock.FromLocal(now);
            DateTime todayRelease = hongKongNow.Date.Add(releaseClock);
            // A short grace period allows the app to start or wake just after release.
            if (hongKongNow < todayRelease.AddSeconds(90))
            {
                return HongKongClock.ToLocal(todayRelease);
            }

            return HongKongClock.ToLocal(todayRelease.AddDays(1));
        }

        public DateTime GetTargetDate(DateTime release)
        {
            return HongKongClock.FromLocal(release).Date.AddDays(this.TargetDayOffset);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }
            return value;
        }

        private static string ValueOr(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string NormalizeClock(string value, string fallback)
        {
            TimeSpan parsed;
            if (TimeSpan.TryParseExact((value ?? string.Empty).Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out parsed) &&
                parsed >= TimeSpan.Zero && parsed < TimeSpan.FromDays(1))
            {
                return parsed.ToString("hh\\:mm", CultureInfo.InvariantCulture);
            }
            return fallback;
        }
    }

    internal sealed class BookingSlot
    {
        public string Name { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string StartText { get; set; }
        public string EndText { get; set; }

        public override string ToString()
        {
            return this.Name + " " + this.StartText + "–" + this.EndText;
        }
    }

    internal static class BookingSlots
    {
        public static readonly string[] Names =
        {
            "第 1 时段",
            "第 2 时段",
            "第 3 时段",
            "自定义时段"
        };

        private static readonly BookingSlotPreset[] LawLibraryPresets =
        {
            new BookingSlotPreset { Name = "第 1 时段", Start = "09:00", End = "13:00" },
            new BookingSlotPreset { Name = "第 2 时段", Start = "13:00", End = "18:00" },
            new BookingSlotPreset { Name = "第 3 时段", Start = "18:00", End = "21:00" },
            new BookingSlotPreset { Name = "自定义时段", IsCustom = true }
        };

        private static readonly BookingSlotPreset[] ChiWahPresets = CreateChiWahPresets();

        public static string[] NamesForArea(string areaName)
        {
            IList<BookingSlotPreset> presets = PresetsForArea(areaName);
            List<string> names = new List<string>();
            foreach (BookingSlotPreset preset in presets)
            {
                names.Add(preset.Name);
            }
            return names.ToArray();
        }

        public static bool IsCustom(string areaName, int index)
        {
            IList<BookingSlotPreset> presets = PresetsForArea(areaName);
            int safeIndex = Math.Max(0, Math.Min(index, presets.Count - 1));
            return presets[safeIndex].IsCustom;
        }

        public static BookingSlot Resolve(BookingConfig config, DateTime targetDate)
        {
            if (IsLawLibrary(config.AreaName) || IsChiWah(config.AreaName))
            {
                IList<BookingSlotPreset> presets = PresetsForArea(config.AreaName);
                int presetIndex = Math.Max(0, Math.Min(config.TimeSlotIndex, presets.Count - 1));
                BookingSlotPreset preset = presets[presetIndex];
                if (preset.IsCustom)
                {
                    return Create(preset.Name, config.CustomStartTime, config.CustomEndTime);
                }
                return Create(preset.Name, preset.Start, preset.End);
            }

            bool weekend = targetDate.DayOfWeek == DayOfWeek.Saturday ||
                targetDate.DayOfWeek == DayOfWeek.Sunday;
            int index = Math.Max(0, Math.Min(config.TimeSlotIndex, 3));

            if (index == 3)
            {
                return Create(Names[3], config.CustomStartTime, config.CustomEndTime);
            }

            if (weekend && index == 2)
            {
                index = 1;
            }

            if (weekend)
            {
                if (index == 0)
                {
                    return Create(Names[0], "09:00", "13:00");
                }
                return Create(Names[1], "13:00", "17:00");
            }

            if (index == 0)
            {
                return Create(Names[0], "08:30", "13:00");
            }
            if (index == 1)
            {
                return Create(Names[1], "13:00", "18:00");
            }
            return Create(Names[2], "18:00", "22:00");
        }

        public static BookingSlot Create(string name, string start, string end)
        {
            TimeSpan startValue;
            TimeSpan endValue;
            if (!TimeSpan.TryParseExact(start, "hh\\:mm", CultureInfo.InvariantCulture, out startValue) ||
                !TimeSpan.TryParseExact(end, "hh\\:mm", CultureInfo.InvariantCulture, out endValue) ||
                endValue <= startValue)
            {
                throw new FormatException("时段格式必须为 HH:mm，且结束时间晚于开始时间。");
            }

            return new BookingSlot
            {
                Name = name,
                Start = startValue,
                End = endValue,
                StartText = start,
                EndText = end
            };
        }

        private static IList<BookingSlotPreset> PresetsForArea(string areaName)
        {
            if (IsLawLibrary(areaName))
            {
                return LawLibraryPresets;
            }
            if (IsChiWah(areaName))
            {
                return ChiWahPresets;
            }

            List<BookingSlotPreset> defaults = new List<BookingSlotPreset>();
            defaults.Add(new BookingSlotPreset { Name = "第 1 时段", Start = "08:30", End = "13:00" });
            defaults.Add(new BookingSlotPreset { Name = "第 2 时段", Start = "13:00", End = "18:00" });
            defaults.Add(new BookingSlotPreset { Name = "第 3 时段", Start = "18:00", End = "22:00" });
            defaults.Add(new BookingSlotPreset { Name = "自定义时段", IsCustom = true });
            return defaults;
        }

        private static BookingSlotPreset[] CreateChiWahPresets()
        {
            List<BookingSlotPreset> presets = new List<BookingSlotPreset>();
            for (int hour = 0; hour < 6; hour++)
            {
                AddHourlyPreset(presets, hour, hour + 1);
            }
            for (int hour = 8; hour < 23; hour++)
            {
                AddHourlyPreset(presets, hour, hour + 1);
            }
            presets.Add(new BookingSlotPreset { Name = "23:00–23:45", Start = "23:00", End = "23:45" });
            presets.Add(new BookingSlotPreset { Name = "自定义时段", IsCustom = true });
            return presets.ToArray();
        }

        private static void AddHourlyPreset(List<BookingSlotPreset> presets, int startHour, int endHour)
        {
            string start = startHour.ToString("00", CultureInfo.InvariantCulture) + ":00";
            string end = endHour.ToString("00", CultureInfo.InvariantCulture) + ":00";
            presets.Add(new BookingSlotPreset
            {
                Name = start + "–" + end,
                Start = start,
                End = end
            });
        }

        private static bool IsLawLibrary(string areaName)
        {
            return (areaName ?? string.Empty).IndexOf("Law", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsChiWah(string areaName)
        {
            return (areaName ?? string.Empty).IndexOf("Chi Wah", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class BookingSlotPreset
    {
        public string Name { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public bool IsCustom { get; set; }
    }

    internal enum RunState
    {
        Waiting,
        Preparing,
        Submitting,
        Retrying,
        Success,
        Failed,
        NeedsAttention
    }

    internal sealed class BookingAttempt
    {
        public DateTime ReleaseAt { get; set; }
        public DateTime TargetDate { get; set; }
        public BookingSlot Slot { get; set; }
    }

    internal sealed class BookingResult
    {
        public bool Success { get; set; }
        public bool AuthenticationRequired { get; set; }
        public bool Retryable { get; set; }
        public string Message { get; set; }
        public string RawResponse { get; set; }

        public static BookingResult Ok(string message)
        {
            return new BookingResult
            {
                Success = true,
                Retryable = false,
                Message = message
            };
        }

        public static BookingResult Fail(string message, bool retryable, bool authenticationRequired)
        {
            return new BookingResult
            {
                Success = false,
                Retryable = retryable,
                AuthenticationRequired = authenticationRequired,
                Message = message
            };
        }
    }

    internal sealed class StatusSnapshot
    {
        public RunState State { get; set; }
        public string Headline { get; set; }
        public string Detail { get; set; }
        public BookingAttempt Attempt { get; set; }
        public int AttemptNumber { get; set; }
        public int MaxAttempts { get; set; }

        public static StatusSnapshot Create(RunState state, string headline, string detail)
        {
            return new StatusSnapshot
            {
                State = state,
                Headline = headline,
                Detail = detail
            };
        }
    }

    internal sealed class AreaPreset
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public override string ToString()
        {
            return this.Name;
        }
    }

    internal static class AreaPresets
    {
        public static readonly AreaPreset[] All =
        {
            new AreaPreset { Name = "Main Library 4/F Study Tables", Url = "https://booking.lib.hku.hk/Secure/FacilityStatusDate.aspx" },
            new AreaPreset { Name = "Main Library Level 3 Study Tables", Url = "https://booking.lib.hku.hk/Secure/FacilityStatusDate.aspx" },
            new AreaPreset { Name = "Deep Quiet Room Study Tables", Url = "https://booking.lib.hku.hk/Secure/FacilityStatusDate.aspx" },
            new AreaPreset { Name = "Law Library Study Tables", Url = "https://booking.lib.hku.hk/Secure/FacilityStatusDate.aspx" },
            new AreaPreset { Name = "Chi Wah Learning Commons Study Rooms", Url = "https://booking.lib.hku.hk/Secure/FacilityStatusDate.aspx" },
            new AreaPreset { Name = "Main Library Single Study Rooms", Url = "https://booking.lib.hku.hk/FView.aspx?lib=3&ftype=31" },
            new AreaPreset { Name = "自定义馆区", Url = "https://booking.lib.hku.hk/" }
        };

        public static AreaPreset Find(string name)
        {
            foreach (AreaPreset preset in All)
            {
                if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }
            return null;
        }

        public static IList<string> Names()
        {
            List<string> names = new List<string>();
            foreach (AreaPreset preset in All)
            {
                names.Add(preset.Name);
            }
            return names;
        }
    }

    internal static class FriendlyState
    {
        public static string Title(RunState state)
        {
            switch (state)
            {
                case RunState.Preparing:
                    return "正在准备预约页面";
                case RunState.Submitting:
                    return "正在提交预约";
                case RunState.Retrying:
                    return "提交失败，正在重试";
                case RunState.Success:
                    return "预约成功";
                case RunState.Failed:
                    return "预约失败";
                case RunState.NeedsAttention:
                    return "需要处理";
                default:
                    return "等待放号";
            }
        }
    }

    internal static class HongKongClock
    {
        private static readonly TimeZoneInfo Zone = ResolveZone();

        public static DateTime FromLocal(DateTime local)
        {
            DateTime specified = DateTime.SpecifyKind(local, DateTimeKind.Local);
            return TimeZoneInfo.ConvertTime(specified, TimeZoneInfo.Local, Zone);
        }

        public static DateTime ToLocal(DateTime hongKongTime)
        {
            DateTime specified = DateTime.SpecifyKind(hongKongTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTime(specified, Zone, TimeZoneInfo.Local);
        }

        private static TimeZoneInfo ResolveZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Asia/Hong_Kong");
                }
                catch
                {
                    return TimeZoneInfo.CreateCustomTimeZone(
                        "Hong Kong Standard Time",
                        TimeSpan.FromHours(8),
                        "Hong Kong Standard Time",
                        "Hong Kong Standard Time");
                }
            }
        }
    }
}
