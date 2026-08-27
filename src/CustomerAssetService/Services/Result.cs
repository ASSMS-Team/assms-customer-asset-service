namespace CustomerAssetService.Services;

public enum ServiceError
{
    None = 0,
    DuplicatePhone = 1,
    NotFound = 2,
    CustomerInactive = 3
}

// Expected failures are returned, not thrown, so the controller's branching is
// explicit and exceptions stay reserved for genuinely exceptional things.
public class Result<T>
{
    private Result(bool isSuccess, T? value, ServiceError error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public ServiceError Error { get; }

    public static Result<T> Success(T value) => new(true, value, ServiceError.None);

    public static Result<T> Failure(ServiceError error) => new(false, default, error);
}
