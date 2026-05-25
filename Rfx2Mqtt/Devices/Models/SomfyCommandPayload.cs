namespace Rfx2Mqtt.Devices.Models;

/// <summary>
/// <b>EN:</b> Somfy command received over MQTT. Expected JSON payload:
/// <c>{ "command": "up|down|stop|program" }</c>.<br/>
/// <b>FR:</b> Commande Somfy reçue via MQTT. Payload JSON attendu :
/// <c>{ "command": "up|down|stop|program" }</c>.
/// </summary>
public class SomfyCommandPayload
{
    /// <summary>
    /// <b>EN:</b> Command to execute: <c>up</c>, <c>down</c>, <c>stop</c> (or <c>my</c>),
    /// <c>program</c> (or <c>prog</c>).<br/>
    /// <b>FR:</b> Commande à exécuter : <c>up</c>, <c>down</c>, <c>stop</c> (ou <c>my</c>),
    /// <c>program</c> (ou <c>prog</c>).
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// <b>EN:</b> Resolves the text command to its RFY byte value, or <c>null</c> if unknown.<br/>
    /// <b>FR:</b> Convertit la commande texte en byte RFY, ou <c>null</c> si inconnue.
    /// </summary>
    public byte? ToRfyCommand() => Command.ToLowerInvariant() switch
    {
        "up" => SomfyCommands.Up,
        "down" => SomfyCommands.Down,
        "stop" or "my" => SomfyCommands.Stop,
        "program" or "prog" => SomfyCommands.Program,
        _ => null
    };
}
