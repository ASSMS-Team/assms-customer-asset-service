namespace CustomerAssetService.Services;

public enum ServiceError
{
    None = 0,
    DuplicatePhone = 1,
    NotFound = 2,
    // The customer this operation acts on, or refers to, is not ACTIVE. Raised
    // by the customer update path about the customer in the route, and by asset
    // creation about the customer in the body - one meaning, so one value.
    CustomerInactive = 3,
    // Distinct from NotFound: NotFound is the addressed resource missing and
    // becomes a 404, whereas this is a customer referenced by a field in the
    // body, which fails as a business rule keyed on that field.
    CustomerNotFound = 4,
    DuplicateSerial = 5
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
