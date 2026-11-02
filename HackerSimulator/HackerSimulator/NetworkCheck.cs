using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace HackerSimulator;

/// <summary>Checks for real internet access (cached for a few seconds).</summary>
public static class NetworkCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static DateTime _checkedAt = DateTime.MinValue;
    private static bool _online;

    public static async Task<bool> IsOnlineAsync()
    {
        if ((DateTime.UtcNow - _checkedAt).TotalSeconds < 4) return _online;

        bool result;
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                result = false;
            }
            else
            {
                using var response = await Http.GetAsync(
                    "http://www.msftconnecttest.com/connecttest.txt",
                    HttpCompletionOption.ResponseHeadersRead);
                result = response.IsSuccessStatusCode;
            }
        }
        catch
        {
            result = false;
        }

        _online = result;
        _checkedAt = DateTime.UtcNow;
        return result;
    }
}