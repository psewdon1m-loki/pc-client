using System.Text.RegularExpressions;

namespace Client.Telemetry;

public sealed partial class SecretRedactor
{
    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var value = VlessUrlRegex().Replace(input, "vless://<redacted>");
        value = UuidRegex().Replace(value, "<uuid-redacted>");
        value = QuerySecretRegex().Replace(value, "$1=<redacted>");
        return value;
    }

    [GeneratedRegex(@"vless://[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VlessUrlRegex();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"(token|uuid|id|pbk|sid|password|key)=([^&\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex QuerySecretRegex();
}

