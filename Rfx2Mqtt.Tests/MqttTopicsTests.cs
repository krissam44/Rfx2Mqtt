using Rfx2Mqtt.Mqtt;

namespace Rfx2Mqtt.Tests;

/// <summary>
/// Tests de la construction centralisée des topics MQTT.
/// </summary>
public class MqttTopicsTests
{
    private readonly MqttTopics _topics = new("rfxcom");

    [Fact]
    public void TopicsFixes()
    {
        Assert.Equal("rfxcom/availability", _topics.BridgeAvailability);
        Assert.Equal("rfxcom/config/permit_join", _topics.PermitJoinState);
        Assert.Equal("rfxcom/devices", _topics.Devices);
        Assert.Equal("rfxcom/info", _topics.BridgeInfo);
        Assert.Equal("rfxcom/command/#", _topics.CommandWildcard);
    }

    [Fact]
    public void TopicsCapteur()
    {
        Assert.Equal("rfxcom/sensor/th/salon", _topics.SensorBase(MqttTopics.KindTh, "salon"));
        Assert.Equal("rfxcom/sensor/th/salon/availability",
            _topics.SensorAvailability(MqttTopics.KindTh, "salon"));
        Assert.Equal("rfxcom/sensor/th/salon/temperature",
            _topics.SensorAttribute(MqttTopics.KindTh, "salon", MqttTopics.AttrTemperature));
    }

    [Fact]
    public void TopicEvenement()
    {
        Assert.Equal("rfxcom/event/somfy/volet_cuisine",
            _topics.Event(MqttTopics.KindSomfy, "volet_cuisine"));
    }

    [Fact]
    public void PrefixePersonnalise()
    {
        var custom = new MqttTopics("maison/rf");
        Assert.Equal("maison/rf/sensor/chacon/prise", custom.SensorBase(MqttTopics.KindChacon, "prise"));
    }
}
