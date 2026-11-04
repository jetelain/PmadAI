using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Pmad.AI.DiffGuard.Cli.Formatting.Sarif;

/// <summary>
/// Generates deterministic RFC 4122 Version 5 UUIDs using SHA-1 hashing.
/// </summary>
public static class DeterministicGuid
{
    /// <summary>
    /// Fixed namespace UUID for URL identifiers defined by RFC 4122.
    /// </summary>
    public static readonly Guid UrlNamespace = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// Fixed namespace UUID derived from "https://github.com/jetelain/PmadAI".
    /// </summary>
    public static readonly Guid DiffGuardNamespace = CreateVersion5(UrlNamespace, "https://github.com/jetelain/PmadAI");

    /// <summary>
    /// Generates a deterministic Version 5 correlation UUID from a value string.
    /// </summary>
    public static Guid GenerateCorrelationGuid(string value)
    {
        return CreateVersion5(DiffGuardNamespace, value ?? string.Empty);
    }

    /// <summary>
    /// Creates a deterministic Version 5 UUID per RFC 4122.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "RFC 4122 UUID version 5 specifies SHA-1")]
    public static Guid CreateVersion5(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        int nameByteCount = Encoding.UTF8.GetByteCount(name);
        int totalLength = 16 + nameByteCount;

        byte[]? rented = null;
        Span<byte> combined = totalLength <= 512
            ? stackalloc byte[totalLength]
            : (rented = ArrayPool<byte>.Shared.Rent(totalLength)).AsSpan(0, totalLength);

        try
        {
            namespaceId.TryWriteBytes(combined[..16], bigEndian: true, out _);
            Encoding.UTF8.GetBytes(name, combined[16..]);

            Span<byte> hash = stackalloc byte[20];
            SHA1.HashData(combined, hash);

            // Version 5 (bits 12-15 of time_hi_and_version set to 0101)
            hash[6] = (byte)((hash[6] & 0x0F) | 0x50);

            // Variant 1 (RFC 4122: bits 6-7 of clock_seq_hi_and_reserved set to 10)
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            return new Guid(hash[..16], bigEndian: true);
        }
        finally
        {
            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}

