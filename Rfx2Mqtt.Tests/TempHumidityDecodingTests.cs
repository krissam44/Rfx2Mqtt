using Rfx2Mqtt.Devices.Handlers;
using Rfx2Mqtt.Devices.Models;

namespace Rfx2Mqtt.Tests;

/// <summary>
/// Tests du décodage des sondes T°/Humidité (paquets 0x52 et 0x54) :
/// température signée, nibbles batterie/signal, canal et filtre anti-parasites.
/// </summary>
public class TempHumidityDecodingTests
{
    #region Température (bit de signe + dixièmes)

    [Theory]
    [InlineData(0x00, 0xDD, 22.1)]   // 221 / 10
    [InlineData(0x00, 0x00, 0.0)]
    [InlineData(0x01, 0x2C, 30.0)]   // (256 + 44) / 10
    [InlineData(0x80, 0x37, -5.5)]   // bit 7 = signe → -(55 / 10)
    [InlineData(0x81, 0x90, -40.0)]  // -(256 + 144) / 10
    public void DecodeTemperature_ValeursNominales(byte high, byte low, double expected)
    {
        var (temperature, _) = TempHumidityHandler.DecodeTemperature(high, low);
        Assert.Equal(expected, temperature, 3);
    }

    [Fact]
    public void DecodeTemperature_BitDeSigne_RetourneIsNegative()
    {
        var (_, negative) = TempHumidityHandler.DecodeTemperature(0x80, 0x37);
        Assert.True(negative);

        var (_, positive) = TempHumidityHandler.DecodeTemperature(0x00, 0x37);
        Assert.False(positive);
    }

    #endregion

    #region Batterie / signal (nibbles)

    [Theory]
    [InlineData(0x96, 9, 6)]  // batterie pleine, bon signal
    [InlineData(0x00, 0, 0)]
    [InlineData(0xF3, 15, 3)]
    [InlineData(0x4A, 4, 10)]
    public void DecodeBatterySignal_SepareLesNibbles(byte value, int battery, int signal)
    {
        var result = TempHumidityHandler.DecodeBatterySignal(value);
        Assert.Equal(battery, result.Battery);
        Assert.Equal(signal, result.Signal);
    }

    [Theory]
    [InlineData(0, "low")]
    [InlineData(3, "low")]   // seuil : 0–3 = pile à remplacer
    [InlineData(4, "ok")]
    [InlineData(9, "ok")]
    public void BatteryLabel_SeuilA3(int level, string expected)
        => Assert.Equal(expected, TempHumidityHandler.BatteryLabel(level));

    #endregion

    #region Canal

    [Fact]
    public void ExtractChannel_OregonTHGN_CanalDansId2()
    {
        // THGN122/132 : canal dans le nibble haut de id2
        var channel = TempHumidityHandler.ExtractChannel(
            TempHumiditySubTypes.THGN122_THGN132, id1: 0xA2, id2: 0x20);
        Assert.Equal(2, channel);
    }

    [Fact]
    public void ExtractChannel_AlectoWH2_ToujoursZero()
    {
        var channel = TempHumidityHandler.ExtractChannel(
            TempHumiditySubTypes.ALECTO_WH2, id1: 0xA2, id2: 0x20);
        Assert.Equal(0, channel);
    }

    [Fact]
    public void ExtractChannel_AutresModeles_CanalDansId1()
    {
        var channel = TempHumidityHandler.ExtractChannel(
            TempHumiditySubTypes.WT260_WT450, id1: 0x30, id2: 0x00);
        Assert.Equal(3, channel);
    }

    #endregion

    #region Filtre anti-parasites

    [Theory]
    [InlineData(22.5, 45, true)]
    [InlineData(-40.0, 0, true)]    // borne basse incluse
    [InlineData(60.0, 100, true)]   // bornes hautes incluses
    [InlineData(-45.0, 50, false)]  // trop froid → parasite
    [InlineData(70.0, 50, false)]   // trop chaud → parasite
    [InlineData(22.0, 101, false)]  // humidité impossible
    public void IsPlausible_BornesTemperatureEtHumidite(double temp, int humidity, bool expected)
    {
        var data = new TempHumidityData { Temperature = temp, Humidity = humidity };
        Assert.Equal(expected, TempHumidityHandler.IsPlausible(data));
    }

    [Theory]
    [InlineData(1013.0, true)]
    [InlineData(700.0, false)]   // < 800 hPa
    [InlineData(1200.0, false)]  // > 1100 hPa
    public void IsPlausible_BornesBarometre(double baro, bool expected)
    {
        var data = new TempHumidityData { Temperature = 20, Humidity = 50, Barometer = baro };
        Assert.Equal(expected, TempHumidityHandler.IsPlausible(data));
    }

    #endregion

    #region Noms de modèles

    [Fact]
    public void GetModelName_SousTypesConnus()
    {
        Assert.Equal("Oregon THGN122/132", TempHumiditySubTypes.GetModelName(0x01));
        Assert.Equal("Bresser Temeo / Alecto WH2", TempHumiditySubTypes.GetModelName(0x0E));
        Assert.Equal("Oregon BTHR918N/968", TempHumBaroSubTypes.GetModelName(0x02));
    }

    [Fact]
    public void GetModelName_SousTypeInconnu_AffichéEnHex()
    {
        Assert.Equal("Inconnu (0xFF)", TempHumiditySubTypes.GetModelName(0xFF));
        Assert.Equal("Inconnu (0x7B)", TempHumBaroSubTypes.GetModelName(0x7B));
    }

    #endregion
}
