using System.Globalization;
using System.Text;

namespace EBICO.Core.Administrative;

/// <summary>
/// Renders the textual customer protocol (<c>PTK</c>, issue #41) as a human-readable plaintext log. Like HAC
/// it is a pure projection over the event log: the customer-visible events are mapped to
/// <see cref="CustomerProtocolEntry"/> by the server and rendered here, one line per entry.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> EBICS does not prescribe an exact PTK line layout; this is a readable,
/// deterministic rendering (ISO-8601 timestamp, severity, order type, return code and message). PTK is a
/// legacy H003/H004 order type — H005 replaces it with HAC.
/// </remarks>
public static class PtkProtocolBuilder
{
    /// <summary>
    /// Builds the PTK plaintext order data from the customer-visible protocol <paramref name="entries"/>
    /// (ordered by sequence), one line per entry.
    /// </summary>
    /// <param name="entries">The customer-visible protocol entries to render (already filtered per customer).</param>
    /// <returns>The PTK protocol as UTF-8 bytes (no BOM), lines separated by <c>\n</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    public static byte[] Build(IReadOnlyList<CustomerProtocolEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.Append(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            builder.Append(" [").Append(entry.Severity).Append(']');

            if (!string.IsNullOrEmpty(entry.OrderType))
            {
                builder.Append(' ').Append(entry.OrderType);
            }

            if (!string.IsNullOrEmpty(entry.ReturnCode))
            {
                builder.Append(' ').Append(entry.ReturnCode);
                if (!string.IsNullOrEmpty(entry.SymbolicName))
                {
                    builder.Append(" (").Append(entry.SymbolicName).Append(')');
                }
            }

            builder.Append(": ").Append(entry.Message).Append('\n');
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
    }
}
