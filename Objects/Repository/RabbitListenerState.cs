namespace NetworkMonitor.Objects.Repository;

public class RabbitListenerState : IRabbitListenerState
{
     public bool IsRabbitConnected { get; set; }
    public string RabbitSetupMessage { get; set; }=" Setup not started yet ";
}
public interface IRabbitListenerState
{
    bool IsRabbitConnected { get; set; }
    string RabbitSetupMessage { get; set; }

   }
