using NetworkMonitor.Objects;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace NetworkMonitor.Objects
{
    public class UserToken
    {
        public UserToken(){}
        public string Name { get; set; }="";
        public string Sub { get; set; }="";

        public string Email { get; set; }="";

        public string Nickname { get; set; }="";
        public string Picture { get; set; }="";

        public DateTime Updated_at { get; set; }


    }
}
