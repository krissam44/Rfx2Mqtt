using Rfx2Mqtt.Devices.Models;

namespace Rfx2Mqtt.Devices.Handlers;

/// <summary>
/// <b>EN:</b> Contract for an RFXCom packet handler. Each device family (Oregon, Somfy, X10
/// Security, Chacon, etc.) implements this interface as a singleton DI service. The Worker
/// dispatches each parsed packet to the first matching handler.<br/>
/// <b>FR:</b> Contrat pour un handler de paquet RFXCom. Chaque famille d'appareils (Oregon, Somfy,
/// X10 Security, Chacon, etc.) implémente cette interface en service DI singleton. Le Worker
/// dispatche chaque paquet parsé vers le premier handler qui matche.
/// </summary>
public interface IPacketHandler
{
    /// <summary>
    /// <b>EN:</b> Packet type bytes (0x52, 0x20, …) handled by this implementation. Used for
    /// documentation / debug — runtime dispatch goes through <see cref="CanHandle"/>.<br/>
    /// <b>FR:</b> Octets de type de paquet (0x52, 0x20, …) gérés par cette implémentation. Sert
    /// pour la doc / le debug — le dispatch runtime passe par <see cref="CanHandle"/>.
    /// </summary>
    IReadOnlyList<byte> SupportedPacketTypes { get; }

    /// <summary>
    /// <b>EN:</b> Returns <c>true</c> if this handler can process <paramref name="packet"/>.<br/>
    /// <b>FR:</b> Renvoie <c>true</c> si ce handler peut traiter <paramref name="packet"/>.
    /// </summary>
    bool CanHandle(RfxComPacket packet);

    /// <summary>
    /// <b>EN:</b> Decodes the packet and returns the MQTT messages to publish (may be empty if the
    /// packet was filtered out — e.g. RF parasite or device ignored by PermitJoin).<br/>
    /// <b>FR:</b> Décode le paquet et renvoie les messages MQTT à publier (peut être vide si le
    /// paquet a été filtré — ex : parasite RF ou device ignoré par PermitJoin).
    /// </summary>
    Task<IEnumerable<MqttMessage>> HandleAsync(RfxComPacket packet);
}

/// <summary>
/// <b>EN:</b> One MQTT message produced by a packet handler.<br/>
/// <b>FR:</b> Un message MQTT produit par un handler de paquet.
/// </summary>
/// <param name="Topic"><b>EN:</b> Full MQTT topic. <b>FR:</b> Topic MQTT complet.</param>
/// <param name="Payload"><b>EN:</b> UTF-8 payload string. <b>FR:</b> Charge utile en UTF-8.</param>
/// <param name="Retain"><b>EN:</b> Retain flag (default false). <b>FR:</b> Flag retain (défaut false).</param>
public record MqttMessage(string Topic, string Payload, bool Retain = false);
