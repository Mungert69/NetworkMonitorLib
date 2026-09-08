using System;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Objects.Api.Services
{
    /// <summary>
    /// Central location for OpenAI-related prompt strings so updates stay consistent.
    /// Shared across NetworkMonitorData and NetworkMonitorLLM.
    /// </summary>
    public static class OpenAiPromptCatalog
    {
        public static class ImageGeneration
        {
            public const string SystemInstruction = @"You generate concise, technical image prompts for a Network Monitoring blog.
Respond ONLY with an image generation prompt that:
1. Focuses on **one key network monitoring concept** (e.g., dashboards, servers, security, cables, data flow)
2. Uses **minimal details** (under 12 words if possible)
3. Specifies **'clean, professional, technical style'**
4. Avoids unnecessary clutter (no multiple overlapping graphs or messy wires)
5. Prefers **dark/light UI themes, server rooms, or abstract data visualizations**
6. Can include **glowing elements for a modern tech feel** (if relevant)

Example format:
- 'A **network dashboard** with glowing metrics, dark theme, clean UI'
- 'A **secure server rack** with blue LED lights, professional style'
- 'An **abstract data flow visualization** on a dark background, minimal details'";

            public const string UserPromptTemplate =
                "Generate a clean, professional image prompt for this Network Monitoring blog post: \"{0}\". Respond ONLY with the prompt.";

            public static string BuildUserPrompt(string blogContent) =>
                string.Format(UserPromptTemplate, blogContent);
        }

        private const string AssistantDisplayName = "Quantum Network Monitor Assistant";
        public static readonly string AssistantLinkMarkdown =
            $"[{AssistantDisplayName}]({AppConstants.FrontendUrl.TrimEnd('/')}/?assistant=open)";

        public static class BlogWriting
        {
            private const string SystemInstructionTemplate =
@"You are a writing assistant specialized in producing human-like blog posts that showcase the Quantum Network Monitor service. The user will provide a title for the blog article, and you will craft the body in Markdown format (omit the title). Highlight how the service—and especially the {0}—helps the reader accomplish the scenario described. When it's natural to mention the assistant, refer to it using the Markdown link {0}. Do not mention or recommend competing products. Whenever you include diagnostics, HTTP responses, or JSON payloads, present them inside fenced code blocks (for example, ```json ... ```); never leave raw braces inline. Respond strictly with the blog content, omitting the title and any extra commentary.";

            private const string GuideUserInputTemplate =
                "Produce a blog post guiding the reader on how to leverage the {0} (part of the Quantum Network Monitor service) to achieve this goal: \"{1}\". Use the focus: \"{2}\" so the article stays tightly aligned with the topic, and naturally encourage readers to explore the assistant via {0} when appropriate. ONLY REPLY WITH the blog content—do not include the title or any extra commentary.";

            public static string SystemInstruction =>
                string.Format(SystemInstructionTemplate, AssistantLinkMarkdown);

            public static string BuildGuideUserInput(string title, string focus) =>
                string.Format(GuideUserInputTemplate, AssistantLinkMarkdown, title, focus);
        }

        public static class BlogQna
        {
            public const string SystemInstruction =
                "You are an assistant that generates Q&A pairs for blog posts. Respond ONLY with a JSON array as described.";

            public const string PromptTemplate = @"
Given the following blog post, generate a list of 3-5 question and answer pairs that a reader might have after reading the post.
Return ONLY a JSON array of objects with the following format:
[
  {{ ""question"": ""..."", ""answer"": ""..."" }},
  ...
]
Blog post:
{0}
";

            public static string BuildPrompt(string blogContent) =>
                string.Format(PromptTemplate, blogContent);
        }
    }
}
