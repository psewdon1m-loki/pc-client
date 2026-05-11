using System.Text.Json;
using Client.Core;

namespace Client.Transport.Xray;

public sealed class XrayConfigValidator
{
    public OperationResult ValidateJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("inbounds", out var inbounds) || inbounds.GetArrayLength() == 0)
            {
                return OperationResult.Fail("Xray config не содержит inbounds.");
            }

            if (!root.TryGetProperty("outbounds", out var outbounds) || outbounds.GetArrayLength() == 0)
            {
                return OperationResult.Fail("Xray config не содержит outbounds.");
            }

            if (!root.TryGetProperty("routing", out var routing) ||
                !routing.TryGetProperty("rules", out var rules) ||
                rules.GetArrayLength() == 0)
            {
                return OperationResult.Fail("Xray config не содержит routing rules.");
            }

            return OperationResult.Ok();
        }
        catch (JsonException ex)
        {
            return OperationResult.Fail($"Xray config содержит некорректный JSON: {ex.Message}");
        }
    }
}

