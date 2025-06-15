namespace NetworkMonitor.Objects;
public class AlertParams
{
    private int _alertThreshold = 5;
    private int _predictThreshold = 1;
    private bool _checkAlerts = true;
    private bool _disablePredictEmailAlert = false;
    private bool _disableMonitorEmailAlert =false;
    private bool _disableEmails =false;

    public int AlertThreshold { get => _alertThreshold; set => _alertThreshold = value; }
    public bool CheckAlerts { get => _checkAlerts; set => _checkAlerts = value; }
    public bool DisableMonitorEmailAlert { get => _disableMonitorEmailAlert; set => _disableMonitorEmailAlert = value; }
    public bool DisablePredictEmailAlert { get => _disablePredictEmailAlert; set => _disablePredictEmailAlert = value; }
    public bool DisableEmails { get => _disableEmails; set => _disableEmails = value; }
   
    public int PredictThreshold { get => _predictThreshold; set => _predictThreshold = value; }
}