using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class FrontMatter
    {
        public FrontMatter(){}
        public string title { get; set; } = "";
        public DateTime date { get; set; }
        public string image { get; set; } = "";
        public string[] categories { get; set; } = new string[0];
        public bool featured { get; set; }
        public bool draft { get; set; }
    }
    public class JsonBlogPost
    {
        public JsonBlogPost(){}
        public FrontMatter frontmatter { get; set; } = new FrontMatter();
        public string content { get; set; } = "";
        public string slug { get; set; } = "";
    }
}