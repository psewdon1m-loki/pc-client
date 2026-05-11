using Client.Profiles;

namespace Client.Tests;

public sealed class VlessParserTests
{
    [Fact]
    public void Parse_RealityTcp_ExtractsRequiredFields()
    {
        var link = "vless://11111111-1111-1111-1111-111111111111@example.com:443?encryption=none&security=reality&type=tcp&sni=www.microsoft.com&fp=chrome&pbk=public-key&sid=abcd&flow=xtls-rprx-vision#RU%20Main";

        var result = new VlessParser().Parse(link);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("RU Main", result.Value!.Name);
        Assert.Equal("example.com", result.Value.Host);
        Assert.Equal(443, result.Value.Port);
        Assert.Equal("reality", result.Value.Security);
        Assert.Equal("tcp", result.Value.Network);
        Assert.Equal("public-key", result.Value.PublicKey);
        Assert.Equal("abcd", result.Value.ShortId);
    }

    [Fact]
    public void Parse_GrpcTls_ExtractsServiceName()
    {
        var link = "vless://11111111-1111-1111-1111-111111111111@grpc.example.com:443?encryption=none&security=tls&type=grpc&sni=grpc.example.com&serviceName=main#grpc";

        var result = new VlessParser().Parse(link);

        Assert.True(result.Success, result.Message);
        Assert.Equal("grpc", result.Value!.Network);
        Assert.Equal("main", result.Value.ServiceName);
    }
}

