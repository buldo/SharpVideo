namespace SharpVideo.Linux.Native;

/// <summary>
/// Enhanced ioctl result with detailed error information and suggestions.
/// </summary>
public readonly struct IoctlResultWithDetails
{
    public bool Success { get; }
    public string OperationName { get; }
    public int ErrorCode { get; }
    public string? ErrorMessage { get; }
    public string? ErrorSuggestion { get; }

    private IoctlResultWithDetails(bool success, string operationName, int errorCode = 0, string? errorMessage = null, string? errorSuggestion = null)
    {
        Success = success;
        OperationName = operationName;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ErrorSuggestion = errorSuggestion;
    }

    public static IoctlResultWithDetails CreateSuccess(string operationName) => 
        new(true, operationName);

    public static IoctlResultWithDetails CreateError(string operationName, int errorCode, string? errorMessage = null, string? errorSuggestion = null) => 
        new(false, operationName, errorCode, errorMessage, errorSuggestion);

    public override string ToString()
    {
        if (Success)
        {
            return $"{OperationName}: Success";
        }

        var result = $"{OperationName}: Failed (Error {ErrorCode})";
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            result += $" - {ErrorMessage}";
        }
        if (!string.IsNullOrEmpty(ErrorSuggestion))
        {
            result += $" | Suggestion: {ErrorSuggestion}";
        }
        return result;
    }
}