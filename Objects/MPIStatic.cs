using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class MPIStatic
    {

        private ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();

        public MPIStatic()
        {
        }

        public MPIStatic(MonitorPingInfo monitorPingInfo)
        {
            //AppID = monitorPingInfo.AppID;
            if (monitorPingInfo != null)
            {
                Address = monitorPingInfo.Address;
                Port = monitorPingInfo.Port;
                Username = monitorPingInfo.Username;
                Password = monitorPingInfo.Password;
                Args = monitorPingInfo.Args;
                MonitorIPID = monitorPingInfo.MonitorIPID;
                EndPointType = monitorPingInfo.EndPointType;
                Timeout = monitorPingInfo.Timeout;
                Enabled = monitorPingInfo.Enabled;
                SiteHash=monitorPingInfo.SiteHash;
            }

            //UserID = monitorPingInfo.UserID;
            //ID = monitorPingInfo.ID;
            //DateStarted = monitorPingInfo.DateStarted;
        }

        /* public string AppID
          {
              get
              {
                  _rwLock.EnterReadLock();
                  try
                  {
                      return _appID;
                  }
                  finally
                  {
                      _rwLock.ExitReadLock();
                  }
              }
              set
              {
                  _rwLock.EnterWriteLock();
                  try
                  {
                      _appID = value;
                  }
                  finally
                  {
                      _rwLock.ExitWriteLock();
                  }
              }
          }*/

        public string Address
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _address;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _address = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public ushort Port
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _port;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _port = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public string? Username
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _username;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _username = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public string? Password
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _password;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _password = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public string? Args
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _args;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _args = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public int MonitorIPID
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _monitorIPID;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _monitorIPID = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public string EndPointType
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _endPointType;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _endPointType = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public int Timeout
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _timeout;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _timeout = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }

        public bool Enabled
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _enabled;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _enabled = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }



        /*  public string UserID
          {
              get
              {
                  _rwLock.EnterReadLock();
                  try
                  {
                      return _userID;
                  }
                  finally
                  {
                      _rwLock.ExitReadLock();
                  }
              }
              set
              {
                  _rwLock.EnterWriteLock();
                  try
                  {
                      _userID = value;
                  }
                  finally
                  {
                      _rwLock.ExitWriteLock();
                  }
              }
          }*/

        /* public int ID
         {
             get
             {
                 _rwLock.EnterReadLock();
                 try
                 {
                     return _id;
                 }
                 finally
                 {
                     _rwLock.ExitReadLock();
                 }
             }
             set
             {
                 _rwLock.EnterWriteLock();
                 try
                 {
                     _id = value;
                 }
                 finally
                 {
                     _rwLock.ExitWriteLock();
                 }
             }
         }*/

        /*public DateTime DateStarted
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _dateStarted;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _dateStarted = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }*/

        // ... Implement the other properties in a similar way ...

        // private string _appID = "";
        private string _address = "";
        private ushort _port;
        private string? _username = "";
        private string? _password = "";
        private string? _args = "";
        private int _monitorIPID;
        private string _endPointType = "";
        private int _timeout;
        private bool _enabled = false;
        private string? _siteHash = null; // New field

        public string? SiteHash
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    return _siteHash;
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
            set
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _siteHash = value;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
        }
    }
}
