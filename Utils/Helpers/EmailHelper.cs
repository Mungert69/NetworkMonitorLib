using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Utils.Helpers
{
    public static class EmailHelper
    {
        public static (bool IsUpdated, string ?Email) NormalizeEmail(string? email)
        {
            if (email == null) return (IsUpdated: false, Email: email);

            int plusIndex = email.IndexOf('+');
            int atIndex = email.IndexOf('@');

            if (plusIndex > -1 && plusIndex < atIndex)
            {
                string normalizedEmail = email.Substring(0, plusIndex) + email.Substring(atIndex);
                return (IsUpdated: true, Email: normalizedEmail);
            }

            return (IsUpdated: false, Email: email);
        }



    }
}