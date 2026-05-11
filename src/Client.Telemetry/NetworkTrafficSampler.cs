using System.Net.NetworkInformation;

namespace Client.Telemetry;

public sealed class NetworkTrafficSampler
{
    private long? _lastBytes;

    public void Start()
    {
        _lastBytes = ReadTotalBytes();
    }

    public long CaptureDelta()
    {
        if (_lastBytes is null)
        {
            return 0;
        }

        var current = ReadTotalBytes();
        var delta = Math.Max(0, current - _lastBytes.Value);
        _lastBytes = current;
        return delta;
    }

    public void Stop()
    {
        _lastBytes = null;
    }

    private static long ReadTotalBytes()
    {
        return NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up
                           && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(item =>
            {
                var statistics = item.GetIPStatistics();
                return statistics.BytesReceived + statistics.BytesSent;
            })
            .DefaultIfEmpty(0)
            .Sum();
    }
}
