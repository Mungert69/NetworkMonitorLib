using NetworkMonitor.Objects;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Windows.Input;


namespace NetworkMonitor.DTOs;
public interface IMonitorPingInfoView : INotifyPropertyChanged
{
    List<MonitorPingInfo> MonitorPingInfos { get; set; }
    List<MPIndicator> MPIndicators { get; }
    MonitorPingInfo SelectedMonitorPingInfo { get; }
    bool HasData { get; }
    bool HasNoData { get; }
    bool IsAnimationOn { get; set; }
    void Update();
    void SelectMonitorPingInfo(int id);
    event Action<MonitorPingInfo> OnShowPopupRequested;
}
public class MonitorPingInfoView : IMonitorPingInfoView
{
    private List<MonitorPingInfo> _monitorPingInfos = new List<MonitorPingInfo>();
    private int _selectedMonitorPingInfoID;

    private bool _hasData = false;

    //public ICommand ShowStatusDetailsCommand { get; private set; }

    public MonitorPingInfoView()
    {
      //  ShowStatusDetailsCommand = new Command<MonitorPingInfo>(ExecuteShowStatusDetailsCommand);
    }

   public bool HasData
    {
        get => _hasData;
        private set
        {
            if (_hasData != value)
            {
                _hasData = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasNoData));
            }
        }
    }

    public bool HasNoData => !HasData;

    public List<MonitorPingInfo> MonitorPingInfos
    {
        get => _monitorPingInfos;
        set
        {
            if (_monitorPingInfos != value)
            {
                _monitorPingInfos = value;
                HasData = value != null && value.Count > 0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MPIndicators));
            }
        }
    }

    private bool _isAnimationOn = false; // default value
    public bool IsAnimationOn
    {
        get => _isAnimationOn;
        set
        {
            if (_isAnimationOn != value)
            {
                _isAnimationOn = value;
                _isAnimationOn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MPIndicators));
            }
        }
    }

    public MonitorPingInfo SelectedMonitorPingInfo
    {
        get => _monitorPingInfos.FirstOrDefault(w => w.MonitorIPID == _selectedMonitorPingInfoID) ?? new MonitorPingInfo();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update()
    {
        // Method logic here
        OnPropertyChanged(nameof(MonitorPingInfos));
        OnPropertyChanged(nameof(SelectedMonitorPingInfo));
    }

    public void SelectMonitorPingInfo(int id)
    {
        if (_selectedMonitorPingInfoID != id)
        {
            _selectedMonitorPingInfoID = id;
            OnPropertyChanged(nameof(SelectedMonitorPingInfo));
        }
    }
    public event Action<MonitorPingInfo> OnShowPopupRequested;

    private void ExecuteShowStatusDetailsCommand(MonitorPingInfo info)
    {
        OnShowPopupRequested?.Invoke(info);
    }

    public List<MPIndicator> MPIndicators
    {
        get
        {
            var mpIndicators = new List<MPIndicator>();
            int totalIndicators = _monitorPingInfos.Count;
            double baseDiameter = 50;
            var sortedMonitorPingInfos = _monitorPingInfos
                            .OrderBy(monitorPingInfo => monitorPingInfo.RoundTripTimeAverage)
                            .ToList();
            foreach (var monitorPingInfo in sortedMonitorPingInfos)
            {
                var mpIndicator = new MPIndicator();
                mpIndicator.CopyMonitorPingInfo(monitorPingInfo);
                mpIndicator.DiameterPixels = Math.Max(10, baseDiameter / Math.Sqrt(totalIndicators));
                mpIndicator.IsAnimated = IsAnimationOn;
                mpIndicators.Add(mpIndicator);
            }

           // double outerCircleRadius = 0.5;
            //double indicatorDiameter = 0.1;
            var distributor = new CircleDistributor();
            var points = mpIndicators.Cast<IPoint>().ToList();
            distributor.DistributeIndicators(points);

            return mpIndicators;
        }
    }


    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
public class MPIndicator : MonitorPingInfo, IPoint
{
    public double XPosition { get; set; } = 0.5d;
    public double YPosition { get; set; } = 0.5d;
    public double DiameterPixels { get; set; } = 15;
    public bool IsAnimated { get; set; } = true;
   
}
