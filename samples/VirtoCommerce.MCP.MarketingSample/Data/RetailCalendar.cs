using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.MarketingSample.Data;

// US and EU retail events that marketing plans around.
public static class RetailCalendar
{
    public const string Retail = "retail";
    public const string B2B = "b2b";
    public const string All = "all";

    public record RetailEvent(string Name, DateTime StartDate, DateTime EndDate, string Note, string Audience)
    {
        public bool Concerns(string storeKind) => Audience == All || Audience == storeKind;
    }

    public static DateTime GetThanksgiving(int year)
    {
        var novemberFirst = new DateTime(year, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstThursday = novemberFirst.AddDays(((int)DayOfWeek.Thursday - (int)novemberFirst.DayOfWeek + 7) % 7);
        return firstThursday.AddDays(21);
    }

    // Black Friday of this year, or of next year once this year's Cyber Monday is over.
    public static DateTime GetNextBlackFriday(DateTime now)
    {
        var blackFriday = GetThanksgiving(now.Year).AddDays(1);
        return blackFriday.AddDays(3) < now.Date ? GetThanksgiving(now.Year + 1).AddDays(1) : blackFriday;
    }

    public static IReadOnlyList<RetailEvent> GetEvents(DateTime from, DateTime to)
    {
        var events = new List<RetailEvent>();

        for (var year = from.Year - 1; year <= to.Year + 1; year++)
        {
            var blackFriday = GetThanksgiving(year).AddDays(1);
            events.Add(new RetailEvent("Singles' Day", Utc(year, 11, 11), Utc(year, 11, 11), "Big in marketplaces and in EU fashion.", Retail));
            events.Add(new RetailEvent("Black Friday", blackFriday, blackFriday.AddDays(2), "Highest traffic of the year; the discount budget is usually 3x a normal week.", All));
            events.Add(new RetailEvent("Cyber Monday", blackFriday.AddDays(3), blackFriday.AddDays(3), "Online-only deals; coupon codes perform best.", All));
            events.Add(new RetailEvent("Holiday shipping cut-off", Utc(year, 12, 18), Utc(year, 12, 18), "Last day for standard delivery before Christmas.", Retail));
            events.Add(new RetailEvent("Christmas", Utc(year, 12, 24), Utc(year, 12, 26), "Stores closed; online gift cards only.", Retail));
            events.Add(new RetailEvent("Year-end B2B budget flush", Utc(year, 12, 1), Utc(year, 12, 31), "B2B buyers spend the rest of the annual budget.", B2B));
            events.Add(new RetailEvent("Winter sales (EU)", Utc(year, 1, 7), Utc(year, 1, 31), "Fashion clearance season.", Retail));
            events.Add(new RetailEvent("Back to school", Utc(year, 8, 1), Utc(year, 9, 10), "Laptops, accessories, printers.", All));
        }

        return events
            .Where(x => x.EndDate >= from && x.StartDate <= to)
            .OrderBy(x => x.StartDate)
            .ToList();
    }

    private static DateTime Utc(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }
}
