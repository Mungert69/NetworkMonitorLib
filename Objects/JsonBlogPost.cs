using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class FrontMatter
    {
        /// <summary>
        /// The title of the blog post.
        /// </summary>
        public string title { get; set; } = "";

        /// <summary>
        /// The date the blog post was created.
        /// </summary>
        public DateTime date { get; set; }

        /// <summary>
        /// The image URL for the blog post.
        /// </summary>
        public string image { get; set; } = "";

        /// <summary>
        /// The categories for the blog post.
        /// </summary>
        public string[] categories { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Whether the blog post is featured.
        /// </summary>
        public bool featured { get; set; }

        /// <summary>
        /// Whether the blog post is a draft.
        /// </summary>
        public bool draft { get; set; }

        /// <summary>
        /// The list of questions QnA for the blog post.
        /// </summary>
        public string[] questions { get; set; } = Array.Empty<string>();

        /// <summary>
        /// The list of answers QnA for the blog post.
        /// </summary>
        public string[] answers { get; set; } = Array.Empty<string>();
    }
    public class JsonBlogPost
    {
        public JsonBlogPost(){}
        public FrontMatter frontmatter { get; set; } = new FrontMatter();
        public string content { get; set; } = "";
        public string slug { get; set; } = "";
    }
}