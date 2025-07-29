using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace NetworkMonitor.Objects;

public class BlogQNA
{
    [Key]
    public int Id { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public virtual int BlogID { get; set; } = 0;

}