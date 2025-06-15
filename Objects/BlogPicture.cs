using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class BlogPicture
    {
        public BlogPicture(){}
        [System.ComponentModel.DataAnnotations.Key]
        public int Id { get; set; }
        public string Name { get; set; }= "";
        public string Path { get; set; }= "";
        public bool IsUsed { get; set; }

        public int BlogId { get; set; }
        public string Category { get; set; }= "";

        public DateTime DateCreated { get; set; }
    }
}