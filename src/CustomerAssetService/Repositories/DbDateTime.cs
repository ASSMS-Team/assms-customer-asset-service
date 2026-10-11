using System.Data.Common;

namespace CustomerAssetService.Repositories;

// TIMESTAMP columns hold UTC (the MySQL server runs in UTC), but MySqlConnector
// returns them as DateTimeKind.Unspecified. Marking them UTC makes the API
// serialize them with a Z, so a browser does not read them as local time.
public static class DbDateTime
{
    public static DateTime ReadUtc(DbDataReader reader, int ordinal) =>
        DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
}
