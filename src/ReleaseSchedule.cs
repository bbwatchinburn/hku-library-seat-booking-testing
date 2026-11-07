using System;

namespace HkuLibrarySeatBooking
{
    internal static class ReleaseSchedule
    {
        public static BookingAttempt Plan(BookingConfig config, DateTime now)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            DateTime release = config.GetNextRelease(now);
            DateTime target = config.GetTargetDate(release);
            return new BookingAttempt
            {
                ReleaseAt = release,
                TargetDate = target,
                Slot = BookingSlots.Resolve(config, target)
            };
        }

        public static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            if (remaining.TotalDays >= 1)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}天 {1:00}:{2:00}:{3:00}",
                    (int)remaining.TotalDays,
                    remaining.Hours,
                    remaining.Minutes,
                    remaining.Seconds);
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}",
                remaining.Hours,
                remaining.Minutes,
                remaining.Seconds);
        }

        public static string FormatTarget(BookingAttempt attempt, string areaName, string seatLabel)
        {
            if (attempt == null)
            {
                return "尚未安排";
            }

            string seat = string.IsNullOrWhiteSpace(seatLabel) ? "自动匹配座位" : seatLabel;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} · {1:MM-dd ddd} · {2}–{3} · {4}",
                areaName,
                attempt.TargetDate,
                attempt.Slot.StartText,
                attempt.Slot.EndText,
                seat);
        }
    }
}
