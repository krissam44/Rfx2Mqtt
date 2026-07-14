using Rfx2Mqtt.Devices.Handlers;
using Rfx2Mqtt.Devices.Models;

namespace Rfx2Mqtt.Tests;

/// <summary>
/// Tests de la conversion des payloads de commande MQTT (texte) vers les octets
/// de commande RFXCom (Lighting2 pour Chacon, RFY pour Somfy).
/// </summary>
public class CommandPayloadTests
{
    #region Chacon / Lighting2

    [Theory]
    [InlineData("on", Lighting2Commands.On)]
    [InlineData("off", Lighting2Commands.Off)]
    [InlineData("set_level", Lighting2Commands.SetLevel)]
    [InlineData("dim", Lighting2Commands.SetLevel)]        // alias
    [InlineData("group_on", Lighting2Commands.GroupOn)]
    [InlineData("group_off", Lighting2Commands.GroupOff)]
    [InlineData("ON", Lighting2Commands.On)]               // insensible à la casse
    public void ChaconPayload_CommandesConnues(string command, byte expected)
    {
        var payload = new ChaconCommandPayload { Command = command };
        Assert.Equal(expected, payload.ToLighting2Command());
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("")]
    public void ChaconPayload_CommandeInconnue_RetourneNull(string command)
    {
        var payload = new ChaconCommandPayload { Command = command };
        Assert.Null(payload.ToLighting2Command());
    }

    #endregion

    #region Somfy / RFY

    [Theory]
    [InlineData("up", SomfyCommands.Up)]
    [InlineData("down", SomfyCommands.Down)]
    [InlineData("stop", SomfyCommands.Stop)]
    [InlineData("my", SomfyCommands.Stop)]                 // alias position "My"
    [InlineData("program", SomfyCommands.Program)]
    [InlineData("prog", SomfyCommands.Program)]            // alias
    [InlineData("UP", SomfyCommands.Up)]                   // insensible à la casse
    public void SomfyPayload_CommandesConnues(string command, byte expected)
    {
        var payload = new SomfyCommandPayload { Command = command };
        Assert.Equal(expected, payload.ToRfyCommand());
    }

    [Theory]
    [InlineData("open")]
    [InlineData("")]
    public void SomfyPayload_CommandeInconnue_RetourneNull(string command)
    {
        var payload = new SomfyCommandPayload { Command = command };
        Assert.Null(payload.ToRfyCommand());
    }

    #endregion

    #region Décodage commande Lighting2 (RX)

    [Theory]
    [InlineData(Lighting2Commands.Off, "off")]
    [InlineData(Lighting2Commands.On, "on")]
    [InlineData(Lighting2Commands.SetLevel, "set_level")]
    [InlineData(Lighting2Commands.GroupOff, "group_off")]
    [InlineData(Lighting2Commands.GroupOn, "group_on")]
    [InlineData(Lighting2Commands.SetGroupLevel, "set_group_level")]
    [InlineData((byte)0x7F, "unknown_0x7F")]
    public void Lighting2Commands_Decode(byte cmd, string expected)
        => Assert.Equal(expected, Lighting2Commands.Decode(cmd));

    [Fact]
    public void Lighting2SubTypes_GetModelName()
    {
        Assert.Equal("Chacon/DIO/AC", Lighting2SubTypes.GetModelName(0x00));
        Assert.Equal("HomeEasy EU", Lighting2SubTypes.GetModelName(0x01));
        Assert.Equal("Inconnu (0x42)", Lighting2SubTypes.GetModelName(0x42));
    }

    #endregion
}
