using Rfx2Mqtt.Configuration;

namespace Rfx2Mqtt.Tests;

/// <summary>
/// Tests du parsing des IDs hex de devices ("01 9E 75 0E" ou "0x01 0x9E ...")
/// et du cache d'octets sur SomfyDevice / ChaconDevice.
/// </summary>
public class HexIdParserTests
{
    [Fact]
    public void Parse_FormatSimple()
    {
        var bytes = HexIdParser.Parse("01 9E 75 0E", 4, "Chacon");
        Assert.Equal([0x01, 0x9E, 0x75, 0x0E], bytes);
    }

    [Fact]
    public void Parse_FormatPrefixe0x()
    {
        var bytes = HexIdParser.Parse("0x01 0x02 0x03", 3, "Somfy");
        Assert.Equal([0x01, 0x02, 0x03], bytes);
    }

    [Fact]
    public void Parse_CasseMixte()
    {
        var bytes = HexIdParser.Parse("0X0a 0Xff 0x1B", 3, "Somfy");
        Assert.Equal([0x0A, 0xFF, 0x1B], bytes);
    }

    [Theory]
    [InlineData("01 02", 3)]         // pas assez d'octets
    [InlineData("01 02 03 04", 3)]   // trop d'octets
    [InlineData("", 3)]              // vide
    public void Parse_MauvaisNombreOctets_FormatException(string id, int expected)
        => Assert.Throws<FormatException>(() => HexIdParser.Parse(id, expected, "Test"));

    [Fact]
    public void Parse_HexInvalide_FormatException()
        => Assert.Throws<FormatException>(() => HexIdParser.Parse("ZZ 01 02", 3, "Test"));

    [Fact]
    public void SomfyDevice_GetIdBytes_CacheEtInvalidationSurChangementId()
    {
        var device = new SomfyDevice { Id = "01 02 03" };

        var first = device.GetIdBytes();
        var second = device.GetIdBytes();
        Assert.Same(first, second); // même instance = cache actif

        device.Id = "04 05 06";
        var third = device.GetIdBytes();
        Assert.Equal([0x04, 0x05, 0x06], third);
    }

    [Fact]
    public void ChaconDevice_GetIdBytes_Parse4Octets()
    {
        var device = new ChaconDevice { Id = "01 9E 75 0E" };
        Assert.Equal([0x01, 0x9E, 0x75, 0x0E], device.GetIdBytes());
    }
}
