using System.Collections.Concurrent;

namespace Rfx2Mqtt.UI;

/// <summary>
/// <b>EN:</b> Thread-safe ring buffer of the most recent RF events received. Blazor pages poll
/// this buffer via a timer instead of subscribing to a C# event — this avoids subscription
/// leaks tied to the Blazor Server circuit lifecycle.<br/>
/// <b>FR:</b> Ring buffer thread-safe des derniers événements RF reçus. Les pages Blazor
/// interrogent ce buffer via un timer plutôt que de s'abonner à un événement C# — cela évite
/// les fuites de souscription liées au cycle de vie des circuits Blazor Server.
/// </summary>
public class UiEventService
{
    private const int Capacity = 100;
    private readonly ConcurrentQueue<RfxEvent> _events = new();

    /// <summary>
    /// <b>EN:</b> UTC timestamp of the last RF frame received (null until the first one).
    /// Used by the /healthz endpoint to detect a silent RFXCom.<br/>
    /// <b>FR:</b> Horodatage UTC de la dernière trame RF reçue (null avant la première).
    /// Utilisé par l'endpoint /healthz pour détecter un RFXCom muet.
    /// </summary>
    public DateTime? LastEventUtc { get; private set; }

    /// <summary>
    /// <b>EN:</b> Adds an event, evicting the oldest entries beyond <see cref="Capacity"/>.<br/>
    /// <b>FR:</b> Ajoute un événement, éjecte les plus anciens au-delà de <see cref="Capacity"/>.
    /// </summary>
    public void Add(RfxEvent evt)
    {
        LastEventUtc = evt.ReceivedAt;
        _events.Enqueue(evt);
        while (_events.Count > Capacity)
            _events.TryDequeue(out _);
    }

    /// <summary>
    /// <b>EN:</b> Returns the events from newest to oldest.<br/>
    /// <b>FR:</b> Renvoie les événements du plus récent au plus ancien.
    /// </summary>
    public IReadOnlyList<RfxEvent> GetRecent() => _events.Reverse().ToArray();
}

/// <summary>
/// <b>EN:</b> Compact representation of an RF event for the Status page UI.<br/>
/// <b>FR:</b> Représentation compacte d'un événement RF pour l'UI de la page Statut.
/// </summary>
/// <param name="ReceivedAt"><b>EN/FR:</b> UTC timestamp. / Horodatage UTC.</param>
/// <param name="PacketType"><b>EN:</b> Friendly packet type label. <b>FR:</b> Libellé lisible du type de paquet.</param>
/// <param name="DeviceId"><b>EN:</b> Device ID as printed in the topic. <b>FR:</b> ID de l'appareil tel qu'il apparaît dans le topic.</param>
/// <param name="DeviceName"><b>EN:</b> Configured friendly name, or <c>null</c> if the device is not yet configured.
/// <b>FR:</b> Nom amical configuré, ou <c>null</c> si l'appareil n'est pas encore configuré.</param>
/// <param name="Summary"><b>EN:</b> One-line debug summary. <b>FR:</b> Résumé debug sur une ligne.</param>
public record RfxEvent(
    DateTime ReceivedAt,
    string PacketType,
    string DeviceId,
    string? DeviceName,
    string Summary
);
