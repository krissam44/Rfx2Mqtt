using Rfx2Mqtt.Devices.Models;

namespace Rfx2Mqtt.Serial;

/// <summary>
/// <b>EN:</b> Event arguments carrying a parsed RFXCom packet from the serial reader thread to
/// any subscribers (the Worker, primarily).<br/>
/// <b>FR:</b> Arguments d'événement transportant un paquet RFXCom parsé depuis le thread de
/// lecture série vers les souscripteurs (principalement le Worker).
/// </summary>
public class PacketReceivedEventArgs : EventArgs
{
    /// <summary>
    /// <b>EN:</b> The parsed packet.<br/>
    /// <b>FR:</b> Le paquet parsé.
    /// </summary>
    public RfxComPacket Packet { get; }

    /// <summary>
    /// <b>EN:</b> Constructor.<br/>
    /// <b>FR:</b> Constructeur.
    /// </summary>
    public PacketReceivedEventArgs(RfxComPacket packet) { Packet = packet; }
}
