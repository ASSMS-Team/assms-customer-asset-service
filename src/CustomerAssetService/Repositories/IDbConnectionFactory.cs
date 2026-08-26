using MySqlConnector;

namespace CustomerAssetService.Repositories;

public interface IDbConnectionFactory
{
    MySqlConnection CreateConnection();
}
