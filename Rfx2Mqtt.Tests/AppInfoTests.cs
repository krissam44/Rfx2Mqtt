using Rfx2Mqtt.Configuration;

namespace Rfx2Mqtt.Tests;

/// <summary>
/// Tests de l'identité applicative lue depuis les attributs d'assembly
/// (version, date de build, copyright — figés à la compilation par le csproj).
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void Version_EstUneVersionSemantique()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void ReleaseDate_EstUneDateIso()
    {
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", AppInfo.ReleaseDate);
    }

    [Fact]
    public void Copyright_MentionneChrissConsulting()
    {
        Assert.Contains("Chriss-Consulting", AppInfo.Copyright);
    }
}
