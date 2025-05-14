using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace NetworkMonitor.Objects
{
    public class StatusItem
    {
        public StatusItem(){}
        public string? Status {get;set;}
        public ushort ID { get; set; }

       

    }
}