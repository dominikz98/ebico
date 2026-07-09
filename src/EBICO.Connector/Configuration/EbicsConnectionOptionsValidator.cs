using Microsoft.Extensions.Options;

namespace EBICO.Connector.Configuration;

/// <summary>
/// Validates <see cref="EbicsConnectionOptions"/> through the options pipeline, so misconfigured
/// connections fail with a clear, aggregated message the first time the options are resolved.
/// </summary>
public sealed class EbicsConnectionOptionsValidator : IValidateOptions<EbicsConnectionOptions>
{
    /// <summary>Validates the given options.</summary>
    /// <param name="name">The options name (ignored; the connector uses the default instance).</param>
    /// <param name="options">The options to validate.</param>
    /// <returns>Success, or a failure listing every problem found.</returns>
    public ValidateOptionsResult Validate(string? name, EbicsConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = EbicsConnection.Validate(options);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
