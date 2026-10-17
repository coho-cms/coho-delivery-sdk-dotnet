namespace CohoCms.Delivery;

/// <summary>
/// A refusal from the delivery API, or an answer the client could not read.
/// </summary>
/// <remarks>
/// Branch on <see cref="Code"/>, which is stable. The server's codes include
/// <c>REF_NOT_FOUND</c>, <c>KEY_REQUIRED</c>, <c>PROJECT_NOT_FOUND</c>, <c>INVALID_QUERY</c>,
/// <c>RESOLUTION_LIMIT</c>, <c>ARTIFACT_UNREADABLE</c> and <c>POOL_UNAVAILABLE</c>; the last
/// two are the server's problem, not the request's. A preview adds <c>UNAUTHENTICATED</c>,
/// for a token that has expired or been revoked, and <c>ACCOUNT_SUSPENDED</c>,
/// <c>ACCOUNT_CLOSED</c> and <c>USER_SUSPENDED</c>. An answer that is not a problem document,
/// such as a proxy's error page, has the code <c>UNEXPECTED_RESPONSE</c>.
/// </remarks>
public sealed class CohoDeliveryException : Exception
{
    public const string UnexpectedResponse = "UNEXPECTED_RESPONSE";

    public CohoDeliveryException(int status, string code, string message, Problem? problem = null)
        : base(message)
    {
        Status = status;
        Code = code;
        Problem = problem;
    }

    public CohoDeliveryException()
        : this(0, UnexpectedResponse, "Delivery answered with no problem document")
    {
    }

    public CohoDeliveryException(string message)
        : this(0, UnexpectedResponse, message)
    {
    }

    public CohoDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = UnexpectedResponse;
    }

    public int Status { get; }

    public string Code { get; }

    /// <summary>The problem document as the server sent it, when there was one.</summary>
    public Problem? Problem { get; }
}
