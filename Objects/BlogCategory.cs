using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace NetworkMonitor.Objects
{
    public class BlogCategory
    {
        public BlogCategory(){}
        [Key]
        public int ID { get; set; }
        public string Category { get; set; }= "";

        public virtual int BlogID{ get; set; } = 0;
    }
   
}