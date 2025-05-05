public class MonitoramentoStatusManager
{
    public bool BancoConectado { get; private set; }
    public bool ClpConectado { get; private set; }

    public void SetBancoConectado(bool status)
    {
        BancoConectado = status;
        Console.WriteLine($"[INFO] Banco conectado: {status}");
    }

    public void SetClpConectado(bool status)
    {
        ClpConectado = status;
        Console.WriteLine($"[INFO] CLP conectado: {status}");
    }

    public bool PodeMonitorar => BancoConectado && ClpConectado;
}

