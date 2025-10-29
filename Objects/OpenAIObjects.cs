using System;
using System.Collections.Generic;
using NetworkMonitor.Objects;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Service.Services.OpenAI
{
    public class FirstData
    {
        public static List<Blog> getData()
        {
            // Read the three files blog-post1.md, blog-post2.md, blog-post3.md into strings blog1text, blog2text, blog3text.
            var files = new List<string>();
            files.Add("blog-post1.md");
            files.Add("blog-post2.md");
            files.Add("blog-post3.md");
            var blog1text = System.IO.File.ReadAllText(files[0]);
            var blog2text = System.IO.File.ReadAllText(files[1]);
            var blog3text = System.IO.File.ReadAllText(files[2]);
            var blogs = new List<Blog>();
            blogs.Add(new Blog()
            {
                Id = 1,
                Hash = "blog-post3",
                Markdown = blog3text,
                IsPublished = true,
                IsFeatured = false,
                IsMainFeatured = true,
                IsImage = false,
                IsOnBlogSite = false
            });
            blogs.Add(new Blog()
            {
                Id = 2,
                Hash = "blog-post2",
                Markdown = blog2text,
                IsPublished = true,
                IsFeatured = true,
                IsMainFeatured = false,
                IsVideo = true,
                VideoUrl = "/img/how-to-login.webm",
                VideoTitle = "How to login to Quantum Network Monitor",
                IsImage = false,
                IsOnBlogSite = false
            });
            blogs.Add(new Blog()
            {
                Id = 3,
                Hash = "blog-post1",
                Markdown = blog1text,
                IsPublished = true,
                IsFeatured = true,
                IsMainFeatured = false,
                IsVideo = true,
                VideoUrl = "/img/how-to-charts.webm",
                VideoTitle = "How to use charts in Quantum Network Monitor",
                IsImage = false,
                IsOnBlogSite = false
            });
            return blogs;
        }
    }
    public class ChatCompletion
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("object")]
        public string? Object { get; set; }
        [JsonPropertyName("created")]
        public long Created { get; set; }
        [JsonPropertyName("model")]
        public string Model { get; set; }="";
        [JsonPropertyName("usage")]
        public ChatCompletionUsage? Usage { get; set; }
        [JsonPropertyName("choices")]
        public List<ChatCompletionChoice>? Choices { get; set; }
    }
    public class ChatCompletionUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
    public class ChatCompletionChoice
    {
        [JsonPropertyName("message")]
        public ChatCompletionMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
        [JsonPropertyName("index")]
        public int Index { get; set; }
    }
    public class ChatCompletionMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
    public class Message
    {
        [JsonPropertyName("role")]
        public string? role { get; set; }
        [JsonPropertyName("content")]
        public string? content { get; set; }
    }
    public class ContentObject
    {
        [JsonPropertyName("model")]
        public string? model { get; set; }
        [JsonPropertyName("messages")]
        public List<Message>? messages { get; set; }
        [JsonPropertyName("temperature")]
        public double temperature { get; set; }
        [JsonPropertyName("presence_penalty")]
        public double presence_penalty { get; set; }
        [JsonPropertyName("frequency_penalty")]
        public double frequency_penalty { get; set; }
        [JsonPropertyName("logit_bias")]
        public Dictionary<int, int> logit_bias { get; set; } = new Dictionary<int, int>();
    }

    public class ResponseRequest
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("input")]
        public List<ResponseMessage>? Input { get; set; }
    }

    public class ResponseMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public List<ResponseMessageContent>? Content { get; set; }
    }

    public class ResponseMessageContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "input_text";

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class OpenAiResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("output")]
        public List<OpenAiResponseOutput>? Output { get; set; }

        [JsonPropertyName("output_text")]
        public List<string>? OutputText { get; set; }
    }

    public class OpenAiResponseOutput
    {
        [JsonPropertyName("content")]
        public List<OpenAiResponseContent>? Content { get; set; }
    }

    public class OpenAiResponseContent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
    public class BlogList
    {
        public string? Content { get; set; }
        public List<string> Categories { get; set; }= new List<string>();
        public DateTime? Date { get; set; }
    }
}
