namespace SolisManager.Extensions;

public static class GeneralExtensions
{
    public static DateTime RoundToHalfHour(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, 
            dateTime.Day, dateTime.Hour, (dateTime.Minute / 30) * 30, 0);
    }
}