using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Models;

namespace Rfx2Mqtt.Serial;

/// <summary>
/// <b>EN:</b> Manages the serial connection to the RFXCom dongle. Responsibilities:
/// <list type="bullet">
///   <item>Open / close the serial port</item>
///   <item>Continuous background read on a dedicated thread (not the threadpool)</item>
///   <item>Send command frames</item>
///   <item>Init sequence (Reset → Get Status → Set Mode)</item>
///   <item>Auto-reconnect on connection loss</item>
/// </list>
/// <br/>
/// <b>FR:</b> Gère la connexion série avec le dongle RFXCom. Responsabilités :
/// <list type="bullet">
///   <item>Ouverture / fermeture du port série</item>
///   <item>Lecture continue en arrière-plan sur un thread dédié (pas le threadpool)</item>
///   <item>Envoi de trames de commande</item>
///   <item>Séquence d'initialisation (Reset → Get Status → Set Mode)</item>
///   <item>Reconnexion automatique en cas de perte de connexion</item>
/// </list>
/// </summary>
public class RfxComSerialService : IDisposable
{
    private readonly ILogger<RfxComSerialService> _logger;
    private readonly RfxComOptions _options;
    private readonly RfxComProtocol _protocol;

    private SerialPort? _serialPort;
    private CancellationTokenSource? _readCts;
    private Thread? _readThread;   // thread dédié — indépendant du thread pool

    // Buffer circulaire pour la lecture
    private readonly byte[] _readBuffer = new byte[4096];
    private int _readBufferPos;

    /// <summary>Événement déclenché à chaque paquet reçu</summary>
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;

    /// <summary>Indique si le RFXCom est connecté et initialisé</summary>
    public bool IsConnected => _serialPort?.IsOpen ?? false;

    /// <summary>Firmware version reçue au status</summary>
    public string? FirmwareVersion { get; private set; }

    public RfxComSerialService(
        ILogger<RfxComSerialService> logger,
        IOptions<RfxComOptions> options,
        RfxComProtocol protocol)
    {
        _logger = logger;
        _options = options.Value;
        _protocol = protocol;
    }

    #region Connexion et Initialisation

    /// <summary>
    /// Ouvre le port série et lance la séquence d'initialisation du RFXCom.
    /// </summary>
    public async Task ConnectAndInitializeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ouverture du port série {Port} à {BaudRate} bauds",
            _options.PortName, _options.BaudRate);

        try
        {
            _serialPort = new SerialPort
            {
                PortName = _options.PortName,
                BaudRate = _options.BaudRate,
                DataBits = 8,
                StopBits = StopBits.One,
                Parity = Parity.None,
                Handshake = Handshake.None,
                ReadTimeout = _options.ReadTimeoutMs,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true
            };

            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            _logger.LogInformation("Port série ouvert avec succès");

            // Lancer la lecture continue en arrière-plan
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readBufferPos = 0;
            _readThread = new Thread(() => ReadLoopSync(_readCts.Token))
            {
                IsBackground = true,
                Name = "RfxCom-Serial-Reader"
            };
            _readThread.Start();

            // Séquence d'initialisation RFXCom
            await InitializeRfxComAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Erreur lors de l'ouverture du port série {Port}", _options.PortName);
            Disconnect();
            throw;
        }
    }

    /// <summary>
    /// Séquence d'initialisation :
    /// 1. Reset
    /// 2. Flush (attente)
    /// 3. Get Status
    /// 4. Set Mode (activation des protocoles)
    /// </summary>
    private async Task InitializeRfxComAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== Initialisation du RFXCom ===");

        if (_options.SkipSetMode)
        {
            // SkipSetMode = conserver la config RFXmngr :
            // on saute aussi le Reset qui effacerait les protocoles configurés
            _logger.LogInformation("SkipSetMode=true → Reset et SetMode ignorés, config RFXmngr conservée");
            _logger.LogInformation("=== RFXCom initialisé avec succès ===");
            return;
        }

        // Étape 1 : Reset
        _logger.LogInformation("Étape 1/3 : Envoi Reset...");
        var resetCmd = _protocol.BuildReset();
        await SendRawAsync(resetCmd, cancellationToken);

        // Attente obligatoire après reset (min 1s, on prend 1.5s)
        _logger.LogInformation("Attente {Delay}ms après reset...", _options.ResetDelayMs);
        await Task.Delay(_options.ResetDelayMs, cancellationToken);

        // Flush du buffer après reset
        _serialPort?.DiscardInBuffer();
        _readBufferPos = 0;

        // Étape 2 : Get Status
        _logger.LogInformation("Étape 2/3 : Envoi Get Status...");
        var statusCmd = _protocol.BuildGetStatus();
        await SendRawAsync(statusCmd, cancellationToken);

        // Attente de la réponse status
        await Task.Delay(500, cancellationToken);

        // Étape 3 : Set Mode
        _logger.LogInformation("Étape 3/3 : Envoi Set Mode (activation protocoles)...");
        _logger.LogInformation("  Protocoles msg3=0x{Msg3:X2} msg4=0x{Msg4:X2} msg5=0x{Msg5:X2}",
            _options.Protocols.ToMsg3(),
            _options.Protocols.ToMsg4(),
            _options.Protocols.ToMsg5());

        var setModeCmd = _protocol.BuildSetMode(_options.Protocols);
        await SendRawAsync(setModeCmd, cancellationToken);

        await Task.Delay(500, cancellationToken);

        _logger.LogInformation("=== RFXCom initialisé avec succès ===");
    }

    #endregion

    #region Envoi

    /// <summary>Envoie une trame brute sur le port série</summary>
    public Task SendRawAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_serialPort is null || !_serialPort.IsOpen)
        {
            _logger.LogWarning("Tentative d'envoi sur un port fermé");
            return Task.CompletedTask;
        }

        _logger.LogDebug("TX >>> {Hex}", RfxComProtocol.FormatHex(data));

        try
        {
            _serialPort.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'envoi de la trame");
            throw;
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Lecture continue

    /// <summary>
    /// Boucle de lecture continue du port série (thread dédié, synchrone).
    /// Lit les octets disponibles, les accumule dans un buffer et tente de parser des paquets complets.
    /// </summary>
    private void ReadLoopSync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Boucle de lecture série démarrée (thread dédié)");

        var tempBuffer = new byte[1024];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_serialPort is null || !_serialPort.IsOpen)
                {
                    Thread.Sleep(100);
                    continue;
                }

                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead == 0)
                {
                    Thread.Sleep(50);
                    continue;
                }

                int bytesRead = _serialPort.Read(tempBuffer, 0, Math.Min(bytesToRead, tempBuffer.Length));
                if (bytesRead == 0) continue;

                // Ajouter au buffer de lecture
                if (_readBufferPos + bytesRead > _readBuffer.Length)
                {
                    _logger.LogWarning("Buffer de lecture plein, reset");
                    _readBufferPos = 0;
                    continue;
                }

                Array.Copy(tempBuffer, 0, _readBuffer, _readBufferPos, bytesRead);
                _readBufferPos += bytesRead;

                // Tenter de parser tous les paquets complets dans le buffer
                ProcessBuffer();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur dans la boucle de lecture série");
                Thread.Sleep(500);
            }
        }

        _logger.LogInformation("Boucle de lecture série terminée");
    }

    /// <summary>Traite le buffer de lecture et extrait les paquets complets</summary>
    private void ProcessBuffer()
    {
        int offset = 0;
        int skippedBytes = 0;

        while (offset < _readBufferPos)
        {
            var (packet, bytesConsumed) = _protocol.TryParse(_readBuffer, offset, _readBufferPos - offset);

            if (bytesConsumed == 0)
                break; // Pas assez de données, on attend la suite

            if (packet is null && bytesConsumed == 1)
            {
                // Re-sync : on saute un octet parasite
                skippedBytes++;
                offset++;
                continue;
            }

            // Si on avait sauté des octets avant ce paquet valide, le signaler
            if (skippedBytes > 0)
            {
                _logger.LogDebug("Re-sync : {Count} octet(s) parasite(s) ignoré(s)", skippedBytes);
                skippedBytes = 0;
            }

            offset += bytesConsumed;

            if (packet is not null)
            {
                _logger.LogDebug("RX <<< {Packet}", packet);
                HandleReceivedPacket(packet);
            }
        }

        if (skippedBytes > 0)
        {
            _logger.LogDebug("Re-sync : {Count} octet(s) parasite(s) ignoré(s)", skippedBytes);
        }

        // Déplacer les données restantes au début du buffer
        if (offset > 0 && offset < _readBufferPos)
        {
            Array.Copy(_readBuffer, offset, _readBuffer, 0, _readBufferPos - offset);
        }
        _readBufferPos -= offset;
    }

    /// <summary>Traitement initial des paquets reçus (status, etc.) puis dispatch</summary>
    private void HandleReceivedPacket(RfxComPacket packet)
    {
        // Traitement spécial du message Status (réponse au GetStatus)
        if (packet.PacketType == PacketTypes.InterfaceMessage && packet.Data.Length >= 9)
        {
            var firmwareVersion = packet.Data[2];
            var hardwareVersion = packet.Data[1];
            FirmwareVersion = $"HW:{hardwareVersion} FW:{firmwareVersion}";
            _logger.LogInformation("RFXCom Status - {FwVersion} - Protocoles actifs: msg3=0x{M3:X2} msg4=0x{M4:X2} msg5=0x{M5:X2}",
                FirmwareVersion,
                packet.Data.Length > 3 ? packet.Data[3] : 0,
                packet.Data.Length > 4 ? packet.Data[4] : 0,
                packet.Data.Length > 5 ? packet.Data[5] : 0);
        }

        // Dispatch vers les handlers externes via l'événement
        PacketReceived?.Invoke(this, new PacketReceivedEventArgs(packet));
    }

    #endregion

    #region Déconnexion

    public void Disconnect()
    {
        _logger.LogInformation("Déconnexion du port série");

        _readCts?.Cancel();

        try
        {
            _readThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch { /* Ignoré */ }

        try
        {
            if (_serialPort?.IsOpen == true)
                _serialPort.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors de la fermeture du port série");
        }

        _serialPort?.Dispose();
        _serialPort = null;
        _readCts?.Dispose();
        _readCts = null;
        _readThread = null;
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    #endregion
}
