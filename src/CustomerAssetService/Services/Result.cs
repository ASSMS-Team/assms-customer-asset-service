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
    DuplicateSerial = 5,
    // The asset this operation acts on is not ACTIVE. Kept separate from
    // CustomerInactive rather than folded into a general "inactive": the two
    // name different resources, and the controller's message has to say which.
    AssetInactive = 6,
    // Staff account creation: the username, email or technician is already linked.
    DuplicateIdentifier = 7,
    // A required field failed validation (e.g. unsupported role).
    ValidationFailed = 8,
    // Technician role requires a linked Dispatch technician UUID.
    TechnicianRequired = 9,
    // The referenced Dispatch technician was not found.
    TechnicianNotFound = 10,
    // Could not reach the Dispatch service to verify the technician.
    DispatchUnavailable = 11,
    // Self-deactivation safeguard: A Manager cannot deactivate their own currently authenticated account.
    SelfDeactivationNotAllowed = 12,
    // Last-Manager safeguard: The system must not allow deactivation of the last active Manager account.
    LastActiveManager = 13
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
