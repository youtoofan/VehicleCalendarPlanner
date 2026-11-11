namespace VehicleCalendarPlanner.Core;

public static class CalendarMath
{
    public static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static (DateOnly Start, DateOnly EndExclusive) MonthGrid(DateOnly date)
    {
        var first = new DateOnly(date.Year, date.Month, 1);
        var start = StartOfWeek(first);
        var endOfMonth = first.AddMonths(1);
        var weeks = (endOfMonth.DayNumber - start.DayNumber + 6) / 7;
        return (start, start.AddDays(weeks * 7));
    }
}
