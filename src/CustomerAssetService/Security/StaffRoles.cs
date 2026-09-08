namespace CustomerAssetService.Security;

public static class StaffRoles
{
    public const string Agent = "Agent";
    public const string Dispatcher = "Dispatcher";
    public const string Technician = "Technician";
    public const string Manager = "Manager";
    public const string All = Agent + "," + Dispatcher + "," + Technician + "," + Manager;
    public const string CustomerEditors = Agent + "," + Manager;
}
