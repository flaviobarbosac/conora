namespace Conora.Domain.Services;

/// <summary>Rule for the "bills paid on time" milestone (age 15): paying on or before the due day counts.</summary>
public static class PaymentPunctuality
{
    /// <summary>Due date for an expense day in a month; days past the month's end fall on its last day.</summary>
    public static DateTime DueDate(int year, int month, int expenseDay)
    {
        var day = Math.Clamp(expenseDay, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>True when the payment happened on or before the due day.</summary>
    public static bool IsOnTime(DateTime paidAt, DateTime due) => paidAt.Date <= due.Date;
}
