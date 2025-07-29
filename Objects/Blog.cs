using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace NetworkMonitor.Objects
{
    public class Blog
    {
        public Blog() { }
        [Key]
        public int Id { get; set; }
        public string Title { get; set; } = "";

        public string Header { get; set; } = "";
        public string Hash { get; set; } = "";
        public string Markdown { get; set; } = "";
        public bool IsFeatured { get; set; } = false;
        public bool IsPublished { get; set; } = true;
        public bool IsMainFeatured { get; set; } = false;
        public bool IsVideo { get; set; } = false;
        public string VideoUrl { get; set; } = "";
        public string VideoTitle { get; set; } = "";
        public bool IsImage { get; set; } = false;
        public string ImageUrl { get; set; } = "";
        public string ImageTitle { get; set; } = "";

        public bool IsOnBlogSite { get; set; } = true;

        public DateTime DateCreated { get; set; } = DateTime.Now;

        public virtual List<BlogCategory> BlogCategories { get; set; } = new List<BlogCategory>();
        public string Question { get; set; } = "";
        public string BriefAnswer { get; set; } = "";
    }

}