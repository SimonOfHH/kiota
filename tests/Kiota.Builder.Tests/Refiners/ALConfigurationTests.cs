using System;

using Kiota.Builder.Refiners;

using Xunit;

namespace Kiota.Builder.Tests.Refiners;

public class ALConfigurationTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        var config = new ALConfiguration();
        config.Validate(); // must not throw
    }

    [Fact]
    public void ThrowsWhenObjectIdRangeStartIsNegative()
    {
        var config = new ALConfiguration { ObjectIdRangeStart = -1 };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void ThrowsWhenObjectIdRangeEndIsBelowStart()
    {
        var config = new ALConfiguration { ObjectIdRangeStart = 60000, ObjectIdRangeEnd = 59999 };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void ThrowsWhenCompanionAppIdIsNotAGuid()
    {
        var config = new ALConfiguration { CompanionAppId = "not-a-guid" };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void ThrowsWhenAppVersionIsNotAVersion()
    {
        var config = new ALConfiguration { AppVersion = "not.a.version" };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void ThrowsWhenCompanionAppVersionIsNotAVersion()
    {
        var config = new ALConfiguration { CompanionAppVersion = "x.y.z" };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void AllowsEmptyOptionalStringValues()
    {
        var config = new ALConfiguration
        {
            CompanionAppId = string.Empty,
            AppVersion = string.Empty,
            CompanionAppVersion = string.Empty,
        };
        config.Validate(); // must not throw
    }
}
