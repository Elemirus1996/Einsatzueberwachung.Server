using System.Runtime.InteropServices;
using Einsatzueberwachung.Domain.Services;
using Xunit;

namespace Einsatzueberwachung.Tests;

/// <summary>Update aus GitHub: nur das Linux-Paket fuer den eigenen Prozessor (x64 bzw. Raspberry Pi/arm64).</summary>
public class UpdatePaketWahlTests
{
    [Theory]
    [InlineData("linux-x64-server-1.14.0.zip", Architecture.X64, true)]
    [InlineData("linux-amd64-server-1.14.0.tar.gz", Architecture.X64, true)]
    [InlineData("linux-arm64-server-1.14.0.zip", Architecture.X64, false)]
    [InlineData("linux-arm64-server-1.14.0.zip", Architecture.Arm64, true)]
    [InlineData("linux-aarch64-server-1.14.0.tar.gz", Architecture.Arm64, true)]
    [InlineData("linux-x64-server-1.14.0.zip", Architecture.Arm64, false)]
    [InlineData("livetracking.zip", Architecture.X64, false)]
    [InlineData("linux-x64-server-1.14.0.exe", Architecture.X64, false)]
    [InlineData("win-x64-server-1.14.0.zip", Architecture.X64, false)]
    [InlineData("linux-x64-server-1.14.0.zip", Architecture.Arm, false)]
    [InlineData("", Architecture.X64, false)]
    public void Waehlt_Paket_passend_zum_Prozessor(string name, Architecture architektur, bool erwartet)
    {
        Assert.Equal(erwartet, GitHubUpdateService.IsLinuxAssetFor(name, architektur));
    }
}
