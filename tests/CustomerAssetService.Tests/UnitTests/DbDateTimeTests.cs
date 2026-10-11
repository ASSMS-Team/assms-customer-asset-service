using System.Data;
using System.Text.Json;
using CustomerAssetService.Repositories;

namespace CustomerAssetService.Tests;

public class DbDateTimeTests
{
    // MySqlConnector hands TIMESTAMP values back with DateTimeKind.Unspecified.
    [Fact]
    public void ReadUtc_MarksADatabaseTimestampAsUtcSoItSerializesWithZ()
    {
        var table = new DataTable();
        table.Columns.Add("created_at", typeof(DateTime));
        table.Rows.Add(new DateTime(2026, 9, 29, 16, 48, 10, DateTimeKind.Unspecified));
        using var reader = table.CreateDataReader();
        reader.Read();

        var value = DbDateTime.ReadUtc(reader, 0);

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 9, 29, 16, 48, 10, DateTimeKind.Utc), value);
        Assert.Equal("\"2026-09-29T16:48:10Z\"", JsonSerializer.Serialize(value));
    }
}
