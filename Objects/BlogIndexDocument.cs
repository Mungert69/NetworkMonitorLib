using System;
using System.Collections.Generic;

namespace NetworkMonitor.Objects
{
    public class BlogIndexDocument
    {
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Content { get; set; } = "";
        public List<string> Categories { get; set; } = new();
        public string Url { get; set; } = "";
        public string Image { get; set; } = "";
        public DateTime? PublishedAt { get; set; }
        public string Author { get; set; } = "";

        public List<float> TitleEmbedding { get; set; } = new();
        public List<float> ContentEmbedding { get; set; } = new();
    }
}
