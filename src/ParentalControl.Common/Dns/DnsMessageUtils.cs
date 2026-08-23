using System.Text;

namespace ParentalControl.Common.Dns;

/// <summary>Minimal raw DNS wire-format helpers - just enough to read the queried domain
/// name out of a request and synthesize an NXDOMAIN reply for blocked domains.
/// Not a general-purpose DNS library.</summary>
public static class DnsMessageUtils
{
    public static bool TryParseQuestionName(byte[] query, out string domainName)
    {
        domainName = string.Empty;
        if (query.Length < 13) return false;

        var offset = 12; // DNS header is always 12 bytes
        var labels = new List<string>();

        while (offset < query.Length)
        {
            int length = query[offset];
            if (length == 0)
            {
                offset++;
                break;
            }
            offset++;
            if (offset + length > query.Length) return false;

            labels.Add(Encoding.ASCII.GetString(query, offset, length));
            offset += length;
        }

        if (labels.Count == 0) return false;
        domainName = string.Join('.', labels);
        return true;
    }

    /// <summary>Builds a synthetic NXDOMAIN response that mirrors the original query's header/question.</summary>
    public static byte[] BuildNxDomainResponse(byte[] query)
    {
        var response = (byte[])query.Clone();
        response[2] = (byte)(response[2] | 0x80); // QR = 1 (this is a response)
        response[3] = 0x83;                       // RA = 1, RCODE = 3 (NXDOMAIN)
        return response;
    }
}
