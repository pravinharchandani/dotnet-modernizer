using System;

namespace LegacyShop.Utils
{
    public static class DateMath
    {
        public static bool IsBusinessDay(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
        }

        public static DateTime AddBusinessDays(DateTime start, int businessDays)
        {
            if (businessDays < 0)
            {
                throw new ArgumentOutOfRangeException("businessDays");
            }

            DateTime current = start;
            int remaining = businessDays;
            while (remaining > 0)
            {
                current = current.AddDays(1);
                if (IsBusinessDay(current))
                {
                    remaining--;
                }
            }

            return current;
        }

        public static int BusinessDaysBetween(DateTime start, DateTime end)
        {
            if (end < start)
            {
                throw new ArgumentException("end must not be earlier than start.");
            }

            int count = 0;
            for (DateTime day = start.Date.AddDays(1); day <= end.Date; day = day.AddDays(1))
            {
                if (IsBusinessDay(day))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
