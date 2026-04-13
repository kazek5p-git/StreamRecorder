using StreamRecorder.Core.Updates;

namespace StreamRecorder.Core.Tests;

public sealed class UpdaterServiceTests
{
    [Theory]
    [InlineData("0.1.6.3", "0.1.6.2", 1)]
    [InlineData("0.1.6.3", "0.1.6.3", 0)]
    [InlineData("0.1.6.3", "0.1.7-dev", -1)]
    [InlineData("0.2.0", "0.2.0-alpha3", 1)]
    [InlineData("v0.1.6.1", "0.1.6", 1)]
    public void CompareVersions_HandlesStableDevAndHotfixCases(string left, string right, int expectedSign)
    {
        var comparison = UpdaterService.CompareVersions(left, right);

        Assert.Equal(expectedSign, Math.Sign(comparison));
    }
}
