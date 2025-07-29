using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;


namespace NetworkMonitor.Objects;

public class BlogQNA
{
    [Key]
    public int Id { get; set; }

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

     [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";

    public virtual int BlogID { get; set; } = 0;

}