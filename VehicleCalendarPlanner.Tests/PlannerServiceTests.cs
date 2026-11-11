using VehicleCalendarPlanner.Core;
using Xunit;

namespace VehicleCalendarPlanner.Tests;

public sealed class PlannerServiceTests
{
    [Fact]
    public async Task FamilyOfFourCanBookFiveVehiclesButChildrenCannotDoubleBookABike()
    {
        var settings = DemoCalendar.CreateSettings();
        var calendar = new DemoCalendar();
        var planner = new PlannerService();
        var date = new DateOnly(2026, 11, 11);

        for (var i = 0; i < settings.Vehicles.Count; i++)
        {
            var result = await planner.BookAsync(calendar, settings, settings.Vehicles[i].Id, settings.People[i % 4].Id, date);
            Assert.False(result.HasConflict);
            Assert.False(result.VerificationFailed);
        }

        var bike = settings.Vehicles[3];
        var exception = await Assert.ThrowsAsync<BookingConflictException>(() =>
            planner.BookAsync(calendar, settings, bike.Id, settings.People[2].Id, date));
        Assert.Contains(settings.People[3].Name, exception.Message);
        Assert.Equal(5, (await calendar.GetEventsAsync(DemoCalendar.CalendarId, date, date.AddDays(1))).Count);

        await planner.BookAsync(calendar, settings, bike.Id, settings.People[2].Id, date.AddDays(1));
        Assert.Single(await calendar.GetEventsAsync(DemoCalendar.CalendarId, date.AddDays(1), date.AddDays(2)));
    }

    [Fact]
    public async Task SimultaneousBookingsOnTheDashboardAllowOnlyOne()
    {
        var settings = DemoCalendar.CreateSettings();
        var calendar = new DemoCalendar();
        var planner = new PlannerService();
        var date = new DateOnly(2026, 11, 11);
        var bike = settings.Vehicles[3];

        async Task<bool> TryBook(int person)
        {
            try
            {
                await planner.BookAsync(calendar, settings, bike.Id, settings.People[person].Id, date);
                return true;
            }
            catch (BookingConflictException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(TryBook(2), TryBook(3));
        Assert.Single(outcomes, x => x);
        Assert.Single(await calendar.GetEventsAsync(DemoCalendar.CalendarId, date, date.AddDays(1)));
    }

    [Fact]
    public async Task CancelMakesTheVehicleAvailableAgain()
    {
        var settings = DemoCalendar.CreateSettings();
        var calendar = new DemoCalendar();
        var planner = new PlannerService();
        var date = new DateOnly(2026, 11, 11);
        var booking = await planner.BookAsync(calendar, settings, settings.Vehicles[3].Id, settings.People[2].Id, date);

        await planner.CancelAsync(calendar, settings, booking.Entry);
        var next = await planner.BookAsync(calendar, settings, settings.Vehicles[3].Id, settings.People[3].Id, date);
        Assert.Equal("Taylor", next.Entry.Reservation?.PersonName);
    }

    [Fact]
    public async Task DisabledPeopleAndVehiclesCannotBeBooked()
    {
        var settings = DemoCalendar.CreateSettings();
        var calendar = new DemoCalendar();
        var planner = new PlannerService();
        var date = new DateOnly(2026, 11, 11);
        settings.People[0].IsActive = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            planner.BookAsync(calendar, settings, settings.Vehicles[0].Id, settings.People[0].Id, date));
        settings.Vehicles[0].IsActive = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            planner.BookAsync(calendar, settings, settings.Vehicles[0].Id, settings.People[1].Id, date));
    }

    [Fact]
    public async Task SettingsPersistAndSampleChangesRemainSeparate()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"family-wheels-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var settings = DemoCalendar.CreateSettings();
            settings.People[0].Name = "Robin";
            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();
            Assert.Equal("Robin", loaded.People[0].Name);
            Assert.Equal(settings.Vehicles[3].Id, loaded.Vehicles[3].Id);
            Assert.Equal("Alex", DemoCalendar.CreateSettings().People[0].Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(directory))
                Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(2026, 11, 11, 2026, 11, 9)]
    [InlineData(2027, 1, 1, 2026, 12, 28)]
    [InlineData(2026, 11, 15, 2026, 11, 9)]
    public void WeekStartsOnMonday(int year, int month, int day, int expectedYear, int expectedMonth, int expectedDay)
    {
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay),
            CalendarMath.StartOfWeek(new DateOnly(year, month, day)));
    }
}
