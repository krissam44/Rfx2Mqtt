using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Models;
using Rfx2Mqtt.Serial;

namespace Rfx2Mqtt.Tests;

/// <summary>
/// Tests du parsing et de la construction des trames binaires RFXCom.
/// Format trame : [length] [type] [subtype] [seqnbr] [data...]
/// </summary>
public class RfxComProtocolTests
{
    private readonly RfxComProtocol _protocol = new();

    #region TryParse — trames valides

    [Fact]
    public void TryParse_TrameTempHumidity_DecodeTousLesChamps()
    {
        // Trame 0x52 (Oregon THGN132) : length=10, subtype=01, seq=0x2A,
        // data = id(0xA2 0x01) tempHi(0x00) tempLo(0xDD) hum(0x37) humStatus(0x01) batSig(0x96)
        byte[] frame = [0x0A, 0x52, 0x01, 0x2A, 0xA2, 0x01, 0x00, 0xDD, 0x37, 0x01, 0x96];

        var (packet, consumed) = _protocol.TryParse(frame, 0, frame.Length);

        Assert.NotNull(packet);
        Assert.Equal(11, consumed);
        Assert.Equal(0x0A, packet.Length);
        Assert.Equal(PacketTypes.TempHumidity, packet.PacketType);
        Assert.Equal(0x01, packet.SubType);
        Assert.Equal(0x2A, packet.SequenceNumber);
        Assert.Equal(7, packet.Data.Length);
        Assert.Equal([0xA2, 0x01, 0x00, 0xDD, 0x37, 0x01, 0x96], packet.Data);
        Assert.Equal(frame, packet.RawBytes);
    }

    [Fact]
    public void TryParse_TrameLighting2_DecodeTousLesChamps()
    {
        // Trame 0x11 (Chacon) : length=11, subtype=00 (AC), seq=5,
        // data = id(01 9E 75 0E) unit(3) cmnd(1=On) level(0) filler+rssi(0x70)
        byte[] frame = [0x0B, 0x11, 0x00, 0x05, 0x01, 0x9E, 0x75, 0x0E, 0x03, 0x01, 0x00, 0x70];

        var (packet, consumed) = _protocol.TryParse(frame, 0, frame.Length);

        Assert.NotNull(packet);
        Assert.Equal(12, consumed);
        Assert.Equal(PacketTypes.Lighting2, packet.PacketType);
        Assert.Equal(8, packet.Data.Length);
    }

    [Fact]
    public void TryParse_BufferIncomplet_AttendSansConsommer()
    {
        // Trame annonce 10 octets de payload mais seuls 5 octets sont arrivés
        byte[] partial = [0x0A, 0x52, 0x01, 0x2A, 0xA2];

        var (packet, consumed) = _protocol.TryParse(partial, 0, partial.Length);

        Assert.Null(packet);
        Assert.Equal(0, consumed); // 0 = on attend la suite du flux
    }

    [Fact]
    public void TryParse_OffsetNonNul_ParseAuBonEndroit()
    {
        byte[] buffer = [0xFF, 0xFF, 0x0A, 0x52, 0x01, 0x2A, 0xA2, 0x01, 0x00, 0xDD, 0x37, 0x01, 0x96];

        var (packet, consumed) = _protocol.TryParse(buffer, 2, buffer.Length - 2);

        Assert.NotNull(packet);
        Assert.Equal(11, consumed);
        Assert.Equal(PacketTypes.TempHumidity, packet.PacketType);
    }

    #endregion

    #region TryParse — resynchronisation sur bruit

    [Fact]
    public void TryParse_LongueurZero_SauteUnOctet()
    {
        // Octet 0x00 résiduel après un reset → ignoré
        var (packet, consumed) = _protocol.TryParse([0x00, 0x0A, 0x52], 0, 3);

        Assert.Null(packet);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void TryParse_LongueurAberrante_SauteUnOctet()
    {
        // Length > 40 = parasite (aucun paquet RFXCom ne dépasse ~28 octets)
        var (packet, consumed) = _protocol.TryParse([0xF0, 0x52, 0x01], 0, 3);

        Assert.Null(packet);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void TryParse_LongueurTropCourte_SauteUnOctet()
    {
        // Length < 3 : impossible (minimum type + subtype + seqnbr)
        var (packet, consumed) = _protocol.TryParse([0x02, 0x52, 0x01], 0, 3);

        Assert.Null(packet);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void TryParse_TypeInconnu_SauteUnOctet()
    {
        var (packet, consumed) = _protocol.TryParse([0x0A, 0x99, 0x01, 0x2A, 0, 0, 0, 0, 0, 0, 0], 0, 11);

        Assert.Null(packet);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void TryParse_SousTypeIncoherent_SauteUnOctet()
    {
        // Type 0x52 avec subtype 0x30 (> 0x20) = données RF alignées par hasard
        var (packet, consumed) = _protocol.TryParse([0x0A, 0x52, 0x30, 0x2A, 0, 0, 0, 0, 0, 0, 0], 0, 11);

        Assert.Null(packet);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void TryParse_BruitPuisTrameValide_SeResynchronise()
    {
        // Un octet parasite (0xF7 = length aberrante... non, 0xF7 > 40 → skip) devant une vraie trame
        byte[] stream = [0xF7, 0x0A, 0x52, 0x01, 0x2A, 0xA2, 0x01, 0x00, 0xDD, 0x37, 0x01, 0x96];

        var (p1, c1) = _protocol.TryParse(stream, 0, stream.Length);
        Assert.Null(p1);
        Assert.Equal(1, c1);

        var (p2, c2) = _protocol.TryParse(stream, c1, stream.Length - c1);
        Assert.NotNull(p2);
        Assert.Equal(11, c2);
        Assert.Equal(PacketTypes.TempHumidity, p2.PacketType);
    }

    [Fact]
    public void TryParse_BufferVide_NeConsommeRien()
    {
        var (packet, consumed) = _protocol.TryParse([], 0, 0);

        Assert.Null(packet);
        Assert.Equal(0, consumed);
    }

    #endregion

    #region Construction de trames — Interface

    [Fact]
    public void BuildReset_Trame14OctetsToutAZero()
    {
        var cmd = _protocol.BuildReset();

        Assert.Equal(14, cmd.Length);
        Assert.Equal(13, cmd[0]);
        Assert.All(cmd.Skip(1), b => Assert.Equal(0x00, b));
    }

    [Fact]
    public void BuildGetStatus_CommandeStatus()
    {
        var cmd = _protocol.BuildGetStatus();

        Assert.Equal(14, cmd.Length);
        Assert.Equal(13, cmd[0]);
        Assert.Equal(PacketTypes.InterfaceControl, cmd[1]);
        Assert.Equal(0x00, cmd[2]);
        Assert.Equal(InterfaceCommands.GetStatus, cmd[4]);
    }

    [Fact]
    public void BuildSetMode_EncodeProtocolesEtFrequence()
    {
        var protocols = new RfxComProtocols
        {
            Lighting4 = true,          // msg3 : 0x08
            LaCrosse = true,           // msg4 : 0x08
            OregonScientific = true,   // msg5 : 0x20
            AC = true,                 // msg5 : 0x04
            X10 = true                 // msg5 : 0x01
        };

        var cmd = _protocol.BuildSetMode(protocols);

        Assert.Equal(14, cmd.Length);
        Assert.Equal(InterfaceCommands.SetMode, cmd[4]);
        Assert.Equal(0x53, cmd[5]); // 433.92 MHz par défaut
        Assert.Equal(0x08, cmd[7]); // msg3
        Assert.Equal(0x08, cmd[8]); // msg4
        Assert.Equal(0x25, cmd[9]); // msg5 = 0x20 | 0x04 | 0x01
    }

    #endregion

    #region Construction de trames — Somfy RFY

    [Fact]
    public void BuildSomfyCommand_TrameConforme()
    {
        var cmd = _protocol.BuildSomfyCommand(0x00, [0x01, 0x02, 0x03], 0x01, SomfyCommands.Up);

        Assert.Equal(13, cmd.Length);
        Assert.Equal(12, cmd[0]);
        Assert.Equal(PacketTypes.Rfy, cmd[1]);
        Assert.Equal(0x00, cmd[2]);
        Assert.Equal(0x01, cmd[4]); // id1
        Assert.Equal(0x02, cmd[5]); // id2
        Assert.Equal(0x03, cmd[6]); // id3
        Assert.Equal(0x01, cmd[7]); // unit code
        Assert.Equal(SomfyCommands.Up, cmd[8]);
    }

    [Fact]
    public void BuildSomfyCommand_IdInvalide_Exception()
    {
        Assert.Throws<ArgumentException>(
            () => _protocol.BuildSomfyCommand(0x00, [0x01, 0x02], 0x01, SomfyCommands.Up));
    }

    #endregion

    #region Construction de trames — Lighting2

    [Fact]
    public void BuildLighting2Command_TrameConforme()
    {
        var cmd = _protocol.BuildLighting2Command(
            0x00, [0x01, 0x9E, 0x75, 0x0E], 3, Lighting2Commands.SetLevel, 12);

        Assert.Equal(12, cmd.Length);
        Assert.Equal(11, cmd[0]);
        Assert.Equal(PacketTypes.Lighting2, cmd[1]);
        Assert.Equal(0x00, cmd[2]);
        Assert.Equal([0x01, 0x9E, 0x75, 0x0E], cmd[4..8]);
        Assert.Equal(3, cmd[8]);                        // unit code
        Assert.Equal(Lighting2Commands.SetLevel, cmd[9]);
        Assert.Equal(12, cmd[10]);                      // level
        Assert.Equal(0x00, cmd[11]);                    // filler + rssi
    }

    [Fact]
    public void BuildLighting2Command_IdInvalide_Exception()
    {
        Assert.Throws<ArgumentException>(
            () => _protocol.BuildLighting2Command(0x00, [0x01, 0x02, 0x03], 1, Lighting2Commands.On, 0));
    }

    #endregion

    #region Séquence et helpers

    [Fact]
    public void GetNextSequence_IncrementeEtBoucleSur255()
    {
        var first = _protocol.GetNextSequence();
        var second = _protocol.GetNextSequence();
        Assert.Equal((byte)(first + 1), second);

        // Avance jusqu'au wrap 255 → 0 (compteur cyclique, unchecked)
        byte last = second;
        for (var i = 0; i < 300; i++)
        {
            var next = _protocol.GetNextSequence();
            Assert.Equal(unchecked((byte)(last + 1)), next);
            last = next;
        }
    }

    [Fact]
    public void FormatHex_OctetsSeparesParEspaces()
    {
        Assert.Equal("0A 52 FF 00", RfxComProtocol.FormatHex([0x0A, 0x52, 0xFF, 0x00]));
    }

    #endregion
}
